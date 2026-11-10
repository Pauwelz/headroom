using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headroom.Tests.Api;

// Runs the real registration (a dummy connection string or OTLP endpoint switches it on) and reads what the exporters get.
// Each test has its own app: the settings differ per test, and the first run needs a database without People.
public class TelemetryTests : IAsyncLifetime
{
    private const string Name = "Zebulon Quillfeather";
    private const string Email = "telemetry-zq@example.com";
    private const string QueryValue = "q-marker-7f3a";

    private readonly HeadroomApp _app = new();
    private readonly FakeEmailSender _mail = new();
    private readonly LockedList<Activity> _spans = new();
    private readonly LockedList<LogRecord> _logs = new();
    private readonly LockedList<Metric> _metrics = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public void WithoutAConnectionStringOrAnOtlpEndpointTheAppRegistersNoTelemetry() =>
        Assert.Null(_app.Factory.Services.GetService<TracerProvider>());

    private const string AzureMonitor = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://localhost:1/";
    private const string Otlp = "http://localhost:1";

    private WebApplicationFactory<Program> StartFactory(string? connectionString = AzureMonitor, string? otlpKey = null, bool endUserId = true) =>
        _app.Factory.WithWebHostBuilder(builder =>
        {
            // UseSetting, not ConfigureAppConfiguration: Program reads the keys before the host builds.
            if (connectionString is not null) builder.UseSetting(HeadroomTelemetry.ConnectionStringKey, connectionString);
            if (otlpKey is not null) builder.UseSetting(otlpKey, Otlp);
            if (!endUserId) builder.UseSetting(HeadroomTelemetry.EndUserIdKey, "false");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IEmailSender>(_mail);
                services.ConfigureOpenTelemetryTracerProvider(t => t.AddInMemoryExporter(_spans));
                services.ConfigureOpenTelemetryMeterProvider(m => m.AddInMemoryExporter(_metrics));
                services.ConfigureOpenTelemetryLoggerProvider(l => l.AddInMemoryExporter(_logs));
                services.Configure<OpenTelemetryLoggerOptions>(o => o.IncludeScopes = true);
            });
        });

    [Fact]
    public async Task ALoginAndAnAllocationChangeExportNoPersonalDataAndNoSqlText()
    {
        var factory = StartFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            var person = new Person { Name = Name, Email = Email, IsPlanner = true };
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = person.Id });
            db.People.Add(person);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = await TestLogin.SignInAsync(factory, _mail, Email);
        client.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(client));
        var code = _mail.Sent.Last().Code;
        var me = (await client.GetFromJsonAsync<MeResponse>($"/api/me?x={QueryValue}", TestContext.Current.CancellationToken))!;
        var created = (await (await client.PostAsJsonAsync("/api/clients", new ClientRequest("Acme Client"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ClientResponse>(TestContext.Current.CancellationToken))!;
        var project = (await (await client.PostAsJsonAsync($"/api/clients/{created.Id}/projects", new CreateProjectRequest("Apollo", "confirmed"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ProjectResponse>(TestContext.Current.CancellationToken))!;
        var allocation = await client.PostAsJsonAsync("/api/allocations",
            new CreateAllocationRequest(me.Id, project.Id, new(2026, 3, 2), new(2026, 3, 6), 50), TestContext.Current.CancellationToken);
        allocation.EnsureSuccessStatusCode();
        await client.GetAsync($"/api/schedule?from=2026-03-01&to=2026-03-31&secret={QueryValue}", TestContext.Current.CancellationToken);

        await FlushAndStopAsync(factory);
        // The Person id is the one personal value that telemetry holds (ADR 0016), as enduser.id on the request span.
        Assert.Contains(_spans.Snapshot(), s => s.Kind == ActivityKind.Server && s.GetTagItem("enduser.id") as string == me.Id.ToString());
        AssertNothingLeaks(true, Email, "Zebulon", "Quillfeather", code, QueryValue);
    }

    // Telemetry:EndUserId=false keeps the Person id out of telemetry.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnMcpToolCallExportsTheToolNameAndThePersonIdWhenSwitchedOn(bool endUserId)
    {
        const string secret = "hr_telemetry-mcp-secret";
        var factory = StartFactory(endUserId: endUserId);
        var person = new Person { Name = Name, Email = Email, IsPlanner = true };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = person.Id });
            db.People.Add(person);
            db.AccessTokens.Add(new AccessToken { Name = "Claude", PersonId = person.Id, CanChange = true, TokenHash = AccessTokenAuth.Hash(secret), LastChangedBy = person.Id });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp") }, http, ownsHttpClient: true);
        await using (var mcp = await McpClient.CreateAsync(transport, McpTests.ClientOptions(), cancellationToken: TestContext.Current.CancellationToken))
        {
            await mcp.CallToolAsync("get_me", cancellationToken: TestContext.Current.CancellationToken);
            // A refused call sends a name and an email as arguments. Neither may reach a span, also on the error path.
            var refused = await mcp.CallToolAsync("add_person", new Dictionary<string, object?>
            {
                ["name"] = Name, ["email"] = Email, ["isPlanner"] = false, ["startDate"] = "2026-03-01",
                ["hours"] = new Dictionary<string, object?> { ["monday"] = 8 },
            }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(refused.IsError);
        }

        await FlushAndStopAsync(factory);
        var spans = _spans.Snapshot();
        // The in-process test client emits client spans on the same source, so only server spans count.
        Assert.Contains(spans, s => s.Kind == ActivityKind.Server && s.GetTagItem("gen_ai.tool.name") as string == "get_me");
        Assert.Equal(endUserId, spans.Any(s => s.Kind == ActivityKind.Server && s.GetTagItem("enduser.id") as string == person.Id.ToString()));
        AssertNothingLeaks(true, [Email, "Zebulon", "Quillfeather", secret, .. endUserId ? Array.Empty<string>() : [person.Id.ToString()]]);
    }

    // The first-run form takes a company, a name and an email, and the first Planner is created from them.
    // OTLP alone, and OTLP next to Azure Monitor, use the same filter. Each OTLP endpoint key switches it on.
    [Theory]
    [InlineData(AzureMonitor, null)]
    [InlineData(null, "OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData(AzureMonitor, "OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData(null, "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    public async Task TheFirstRunExportsNoPersonalData(string? connectionString, string? otlpKey)
    {
        var factory = StartFactory(connectionString, otlpKey);
        await FirstRunAsync(factory);
        var id = PersonId(factory);
        await FlushAndStopAsync(factory);
        AssertNothingLeaks(expectSqlSpans: connectionString is not null, // only the Azure Monitor distro adds the SQL spans
            id, "first-run-ob@example.com", "Zebulon", "Quillfeather", "Hollowbrook", _mail.Sent.Last().Code);
    }

    private async Task FirstRunAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var fields = new[] { ("company", "Hollowbrook Holdings"), ("name", Name), ("email", "first-run-ob@example.com") };
        await TestLogin.PostFrom(client, "/first-run", "/first-run", fields);
        var done = await TestLogin.PostFrom(client, "/first-run", "/first-run/verify", [.. fields, ("code", _mail.Sent.Last().Code)]);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, done.StatusCode);
    }

    private static string PersonId(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Single().Id.ToString();
    }

    private async Task FlushAndStopAsync(WebApplicationFactory<Program> factory)
    {
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush();
        factory.Services.GetRequiredService<LoggerProvider>().ForceFlush();

        await factory.DisposeAsync(); // stops the background services, so nothing adds to the lists while the test reads them
    }

    private void AssertNothingLeaks(bool expectSqlSpans, params string[] secrets)
    {
        var (spans, logs, metrics) = (_spans.Snapshot(), _logs.Snapshot(), _metrics.Snapshot());
        Assert.Contains(spans, s => s.GetTagItem("http.route") is not null);
        if (expectSqlSpans)
            Assert.Contains(spans, s => s.GetTagItem("db.system.name") is not null || s.GetTagItem("db.system") is not null);
        var texts = new List<string>();
        foreach (var span in spans)
        {
            texts.Add(span.DisplayName);
            texts.AddRange(span.TagObjects.SelectMany(t => new[] { t.Key, t.Value?.ToString() ?? "" }));
            texts.AddRange(span.Events.SelectMany(e => e.Tags.SelectMany(t => new[] { t.Key, t.Value?.ToString() ?? "" })));
            Assert.Null(span.GetTagItem("url.query"));
            Assert.Null(span.GetTagItem("url.full"));
            Assert.Null(span.GetTagItem("db.statement"));
            Assert.Null(span.GetTagItem("db.query.text"));
        }
        foreach (var log in logs)
        {
            texts.Add(log.FormattedMessage ?? "");
            texts.Add(log.Body ?? "");
            texts.AddRange(log.Attributes?.Select(a => $"{a.Key}={a.Value}") ?? []);
            log.ForEachScope((scope, list) => { foreach (var a in scope) list.Add($"{a.Key}={a.Value}"); }, texts);
        }
        foreach (var metric in metrics)
        {
            texts.Add(metric.Name);
            foreach (var point in metric.GetMetricPoints())
                foreach (var tag in point.Tags) texts.Add($"{tag.Key}={tag.Value}");
        }

        var everything = string.Join('\n', texts);
        Assert.NotEmpty(logs);
        foreach (var secret in secrets.Concat(["SELECT ", "INSERT ", "UPDATE ", "FROM ["]))
            Assert.DoesNotContain(secret, everything, StringComparison.OrdinalIgnoreCase);
    }

    // The exporters add from background threads, also while the test reads. A lock on add and read keeps the read safe.
    private sealed class LockedList<T> : Collection<T>
    {
        private readonly Lock _gate = new();

        protected override void InsertItem(int index, T item)
        {
            lock (_gate) base.InsertItem(index, item);
        }

        protected override void ClearItems()
        {
            lock (_gate) base.ClearItems();
        }

        public T[] Snapshot()
        {
            lock (_gate) return [.. Items];
        }
    }
}
