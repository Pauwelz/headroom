using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Events;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Headroom.Tests.Api;

// Each counter shows after its action. The Meter is static, so parallel test classes add to it too: assert "at least".
public class MetricsTests : IAsyncLifetime
{
    private const string Email = "metrics-mm@example.com";
    private readonly HeadroomApp _app = new();
    private readonly FakeEmailSender _mail = new();
    private readonly List<Metric> _metrics = [];
    private readonly StubReceiver _receiver = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private async Task<WebApplicationFactory<Program>> StartAsync(IEmailSender? mail = null, bool seed = true)
    {
        var factory = _app.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(HeadroomTelemetry.ConnectionStringKey,
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://localhost:1/");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IEmailSender>(mail ?? _mail);
                services.ConfigureOpenTelemetryMeterProvider(m => m.AddInMemoryExporter(_metrics));
                services.AddHttpClient(EventSender.ClientName).ConfigurePrimaryHttpMessageHandler(() => _receiver);
            });
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (seed && !db.People.Any())
        {
            var person = new Person { Name = "Mia Metrics", Email = Email, IsPlanner = true };
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = person.Id });
            db.People.Add(person);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return factory;
    }

    // The sum of a counter over all points with the given tag, or null when the counter has no such point.
    private long? Sum(WebApplicationFactory<Program> factory, string name, string key, string value)
    {
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush();
        long? sum = null;
        foreach (var metric in _metrics.Where(m => m.Name == name))
            foreach (var point in metric.GetMetricPoints())
                foreach (var tag in point.Tags)
                    if (tag.Key == key && tag.Value?.ToString() == value) sum = (sum ?? 0) + point.GetSumLong();
        return sum;
    }

    [Fact]
    public async Task LoginsAndLoginCodeEmailsAreCounted()
    {
        using var factory = await StartAsync();
        await TestLogin.SignInAsync(factory, _mail, Email);
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await TestLogin.Post(anonymous, "/login/verify", ("email", Email), ("code", "000000"));

        Assert.True(Sum(factory, "headroom.logins", "result", "success") >= 1);
        Assert.True(Sum(factory, "headroom.logins", "result", "failed") >= 1);
        Assert.True(Sum(factory, "headroom.login_code_emails", "result", "sent") >= 1);
    }

    [Fact]
    public async Task AFailedLoginCodeEmailIsCounted()
    {
        using var factory = await StartAsync(new ThrowingEmailSender());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await TestLogin.Post(client, "/login", ("email", Email));

        Assert.True(Sum(factory, "headroom.login_code_emails", "result", "failed") >= 1);
    }

    [Fact]
    public async Task LoginCodeEmailsOfTheFirstRunAreCounted()
    {
        foreach (var (mail, result) in new (IEmailSender, string)[] { (_mail, "sent"), (new ThrowingEmailSender(), "failed") })
        {
            using var factory = await StartAsync(mail, seed: false);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            await TestLogin.PostFrom(client, "/first-run", "/first-run", ("company", "Acme"), ("name", "Ann Planner"), ("email", "first-run-fr@example.com"));

            Assert.True(Sum(factory, "headroom.login_code_emails", "result", result) >= 1);
        }
    }

    [Fact]
    public async Task ChangesApiCallsAndMcpCallsAreCounted()
    {
        using var factory = await StartAsync();
        var client = await TestLogin.SignInAsync(factory, _mail, Email);
        client.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(client));
        var me = (await client.GetFromJsonAsync<MeResponse>("/api/me", TestContext.Current.CancellationToken))!;
        var created = (await (await client.PostAsJsonAsync("/api/clients", new ClientRequest("Acme Client"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ClientResponse>(TestContext.Current.CancellationToken))!;
        var project = (await (await client.PostAsJsonAsync($"/api/clients/{created.Id}/projects", new CreateProjectRequest("Apollo", "tentative"), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<ProjectResponse>(TestContext.Current.CancellationToken))!;
        (await client.PostAsJsonAsync("/api/allocations",
            new CreateAllocationRequest(me.Id, project.Id, new(2026, 3, 2), new(2026, 3, 6), 50), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("Apollo", created.Id, "confirmed"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await client.PostAsync("/mcp", null, TestContext.Current.CancellationToken); // no token: answered 401, still a call

        Assert.True(Sum(factory, "headroom.record_changes", "type", "headroom.Allocation.created") >= 1);
        Assert.True(Sum(factory, "headroom.record_changes", "type", "headroom.Project.changed") >= 1);
        Assert.True(Sum(factory, "headroom.requests", "surface", "api") >= 1);
        Assert.True(Sum(factory, "headroom.requests", "surface", "mcp") >= 1);
    }

    [Fact]
    public async Task WebhookDeliveriesAreCountedAndTheBacklogIsReported()
    {
        using var factory = await StartAsync();
        var hook = new Webhook { Name = "Flow", Url = "https://receiver.example.com/hook", Secret = EventSender.NewSecret() };
        Event Make(DateTimeOffset? next) => new()
        {
            WebhookId = hook.Id, Type = "headroom.Allocation.created", Payload = "{}", DateCreated = DateTimeOffset.UtcNow, NextAttemptAt = next,
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.Webhooks.Add(hook);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.Events.Add(Make(null));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _receiver.Status = HttpStatusCode.OK;
            await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(TestContext.Current.CancellationToken);
            _receiver.Status = HttpStatusCode.InternalServerError;
            db.Events.Add(Make(null));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(Sum(factory, "headroom.webhook_deliveries", "result", "success") >= 1);
        Assert.True(Sum(factory, "headroom.webhook_deliveries", "result", "failed") >= 1);
        // The failed Event waits for its next attempt: it is the backlog. The gauge is read directly: other apps in parallel tests share the Meter name.
        var gauge = factory.Services.GetServices<IHostedService>().OfType<EventBacklogGauge>().Single();
        Assert.Equal(1, gauge.Count());
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct) =>
            throw new InvalidOperationException("mail down");
    }

    private sealed class StubReceiver : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(Status));
    }
}
