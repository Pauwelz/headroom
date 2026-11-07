using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Events;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Headroom.Tests.Api;

// Each counter grows after its action. The Meter is static, so parallel test classes add to it too: assert "grows", not exact values.
public class MetricsTests(MetricsApp app) : IClassFixture<MetricsApp>, IAsyncLifetime
{
    private const string Email = "metrics-mm@example.com";
    private readonly WebApplicationFactory<Program> _factory = app.Factory;
    private readonly FakeEmailSender _mail = app.Mail;

    public async ValueTask InitializeAsync()
    {
        app.MailFails = false;
        await SeedAsync(app);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task SeedAsync(MetricsApp app)
    {
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var person = new Person { Name = "Mia Metrics", Email = Email, IsPlanner = true };
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = person.Id });
        db.People.Add(person);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoginsAndLoginCodeEmailsAreCounted()
    {
        var (success, failed, sent) = (app.Total("headroom.logins", "result", "success"), app.Total("headroom.logins", "result", "failed"),
            app.Total("headroom.login_code_emails", "result", "sent"));
        await TestLogin.SignInAsync(_factory, _mail, Email);
        var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await TestLogin.Post(anonymous, "/login/verify", ("email", Email), ("code", "000000"));

        Assert.True(app.Total("headroom.logins", "result", "success") > success);
        Assert.True(app.Total("headroom.logins", "result", "failed") > failed);
        Assert.True(app.Total("headroom.login_code_emails", "result", "sent") > sent);
    }

    [Fact]
    public async Task AFailedLoginCodeEmailIsCounted()
    {
        var failed = app.Total("headroom.login_code_emails", "result", "failed");
        app.MailFails = true;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await TestLogin.Post(client, "/login", ("email", Email));

        Assert.True(app.Total("headroom.login_code_emails", "result", "failed") > failed);
    }

    // The first run needs a database without People, so this test has its own app.
    [Fact]
    public async Task LoginCodeEmailsOfTheFirstRunAreCounted()
    {
        await using var empty = new MetricsApp();
        await empty.InitializeAsync();
        foreach (var (fails, result) in new[] { (false, "sent"), (true, "failed") })
        {
            var before = empty.Total("headroom.login_code_emails", "result", result);
            empty.MailFails = fails;
            var client = empty.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            await TestLogin.PostFrom(client, "/first-run", "/first-run", ("company", "Acme"), ("name", "Ann Planner"), ("email", "first-run-fr@example.com"));

            Assert.True(empty.Total("headroom.login_code_emails", "result", result) > before);
        }
    }

    [Fact]
    public async Task ChangesApiCallsAndMcpCallsAreCounted()
    {
        long Created() => app.Total("headroom.record_changes", "type", "headroom.Allocation.created");
        long Changed() => app.Total("headroom.record_changes", "type", "headroom.Project.changed");
        long Api() => app.Total("headroom.requests", "surface", "api");
        long Mcp() => app.Total("headroom.requests", "surface", "mcp");
        var (created0, changed0, api0, mcp0) = (Created(), Changed(), Api(), Mcp());
        var client = await TestLogin.SignInAsync(_factory, _mail, Email);
        client.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(client));
        var me = (await client.GetFromJsonAsync<MeResponse>("/api/me", TestContext.Current.CancellationToken))!;
        var created = (await (await client.PostAsJsonAsync("/api/clients", new ClientRequest($"Metrics Client {Guid.NewGuid():N}"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ClientResponse>(TestContext.Current.CancellationToken))!;
        var project = (await (await client.PostAsJsonAsync($"/api/clients/{created.Id}/projects", new CreateProjectRequest("Apollo", "tentative"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ProjectResponse>(TestContext.Current.CancellationToken))!;
        (await client.PostAsJsonAsync("/api/allocations",
            new CreateAllocationRequest(me.Id, project.Id, new(2026, 3, 2), new(2026, 3, 6), 50), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("Apollo", created.Id, "confirmed"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await client.PostAsync("/mcp", null, TestContext.Current.CancellationToken); // no token: answered 401, still a call

        Assert.True(Created() > created0);
        Assert.True(Changed() > changed0);
        Assert.True(Api() > api0);
        Assert.True(Mcp() > mcp0);
    }

    [Fact]
    public async Task WebhookDeliveriesAreCountedAndTheBacklogIsReported()
    {
        var (success, failed) = (app.Total("headroom.webhook_deliveries", "result", "success"), app.Total("headroom.webhook_deliveries", "result", "failed"));
        var hook = new Webhook { Name = "Flow", Url = "https://receiver.example.com/hook", Secret = EventSender.NewSecret() };
        Event Make(DateTimeOffset? next) => new()
        {
            WebhookId = hook.Id, Type = "headroom.Allocation.created", Payload = "{}", DateCreated = DateTimeOffset.UtcNow, NextAttemptAt = next,
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.Webhooks.Add(hook);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.Events.Add(Make(null));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            app.Receiver.Status = HttpStatusCode.OK;
            await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(TestContext.Current.CancellationToken);
            app.Receiver.Status = HttpStatusCode.InternalServerError;
            db.Events.Add(Make(null));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(app.Total("headroom.webhook_deliveries", "result", "success") > success);
        Assert.True(app.Total("headroom.webhook_deliveries", "result", "failed") > failed);
        // The failed Event waits for its next attempt: it is the backlog. The gauge is read directly: other apps in parallel tests share the Meter name.
        var gauge = _factory.Services.GetServices<IHostedService>().OfType<EventBacklogGauge>().Single();
        Assert.Equal(1, gauge.Count());
    }
}

// One app with telemetry on for all MetricsTests. The Event sender service is off, so it cannot race the tests that send by hand.
public sealed class MetricsApp : HeadroomApp
{
    private readonly List<Metric> _metrics = [];
    private readonly Lock _gate = new();

    public bool MailFails { get; set; }

    public StubReceiver Receiver { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(HeadroomTelemetry.ConnectionStringKey,
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://localhost:1/");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEmailSender>(new SwitchableEmailSender(this));
            services.ConfigureOpenTelemetryMeterProvider(m => m.AddInMemoryExporter(_metrics));
            services.AddHttpClient(EventSender.ClientName).ConfigurePrimaryHttpMessageHandler(() => Receiver);
            services.Remove(services.Single(d => d.ImplementationType == typeof(EventSenderService)));
        });
    }

    // The current sum of a counter over all points with the given tag. The exporter is cumulative, so one fresh export holds the total.
    public long Total(string name, string key, string value)
    {
        lock (_gate)
        {
            _metrics.Clear();
            Factory.Services.GetRequiredService<MeterProvider>().ForceFlush();
            long sum = 0;
            foreach (var metric in _metrics.Where(m => m.Name == name))
                foreach (var point in metric.GetMetricPoints())
                    foreach (var tag in point.Tags)
                        if (tag.Key == key && tag.Value?.ToString() == value) sum += point.GetSumLong();
            return sum;
        }
    }

    private sealed class SwitchableEmailSender(MetricsApp app) : IEmailSender
    {
        public Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct) =>
            app.MailFails ? throw new InvalidOperationException("mail down") : app.Mail.SendLoginCodeAsync(toAddress, companyName, code, ct);
    }
}

public sealed class StubReceiver : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(Status));
}
