using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Headroom.Tests.Api;

public class WebhookEventsTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/webhooks";
    private readonly FakeEmailSender _mail = new();
    private readonly CapturingHandler _receiver = new();
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _planner = null!;
    private Guid _floId;
    private Guid _projectId;

    public WebhookEventsTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEmailSender>(_mail);
            // Tests drive the sender by hand, so the timer must not send Events in between.
            services.RemoveAll<IHostedService>();
            services.AddHttpClient(EventSender.ClientName).ConfigurePrimaryHttpMessageHandler(() => _receiver);
        }));
    }

    public async ValueTask InitializeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            if (!db.People.Any())
            {
                var ann = new Person { Name = "Ann Planner", Email = "wh-ann@example.com", IsPlanner = true };
                var flo = new Person { Name = "Flo Good", Email = "wh-flo@example.com" };
                var client = new Headroom.Data.Client { Name = "Acme Corp" };
                db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
                db.People.AddRange(ann, flo);
                db.Clients.Add(client);
                db.Projects.Add(new Project { ClientId = client.Id, Name = "Website" });
                await db.SaveChangesAsync();
            }
            _floId = db.People.Single(p => p.Email == "wh-flo@example.com").Id;
            _projectId = db.Projects.Single(p => p.Name == "Website").Id;
        }
        _planner = await TestLogin.SignInAsync(_factory, _mail, "wh-ann@example.com");
        _planner.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_planner));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    private async Task<CreatedWebhookResponse> AddWebhook(string name = "Flow")
    {
        var added = await _planner.PostAsJsonAsync(Url, new WebhookRequest(name, "https://receiver.example.com/hook"));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        return (await added.Content.ReadFromJsonAsync<CreatedWebhookResponse>())!;
    }

    private async Task RemoveAllWebhooks()
    {
        foreach (var w in (await _planner.GetFromJsonAsync<List<WebhookResponse>>(Url))!)
            await _planner.DeleteAsync($"{Url}/{w.Id}");
    }

    private async Task<List<Event>> EventsOf(Guid webhookId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events
            .Where(e => e.WebhookId == webhookId).OrderBy(e => e.DateCreated).ToListAsync();
    }

    private Task<HttpResponseMessage> Create(int day) => _planner.PostAsJsonAsync("/api/allocations",
        new CreateAllocationRequest(_floId, _projectId, new(2026, 3, day), new(2026, 3, day), 50));

    [Fact]
    public async Task PlannerAddsRenamesAndDeletesAWebhookAndSeesTheSecretOnce()
    {
        var created = await AddWebhook("First");
        Assert.StartsWith("whsec_", created.Secret);

        var renamed = await _planner.PutAsJsonAsync($"{Url}/{created.Id}", new RenameWebhookRequest("Second"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        var listText = await _planner.GetStringAsync(Url, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Second", listText);
        Assert.DoesNotContain(created.Secret, listText);

        Assert.Equal(HttpStatusCode.NoContent, (await _planner.DeleteAsync($"{Url}/{created.Id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.DoesNotContain("Second", await _planner.GetStringAsync(Url, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithoutWebhooksNoEventIsStored()
    {
        await RemoveAllWebhooks();
        Assert.Equal(HttpStatusCode.NoContent, (await Create(2)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AllocationCreateChangeAndDeleteStoreOneCloudEventPerWebhook()
    {
        await RemoveAllWebhooks();
        var one = await AddWebhook("One");
        var two = await AddWebhook("Two");

        Assert.Equal(HttpStatusCode.NoContent, (await Create(10)).StatusCode);
        Guid id;
        using (var scope = _factory.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Allocations
                .SingleAsync(a => a.StartDate == new DateOnly(2026, 3, 10) && a.PersonId == _floId, cancellationToken: TestContext.Current.CancellationToken)).Id;
        await _planner.PutAsJsonAsync($"/api/allocations/{id}", new UpdateAllocationRequest(new(2026, 3, 10), new(2026, 3, 10), 75), cancellationToken: TestContext.Current.CancellationToken);
        await _planner.DeleteAsync($"/api/allocations/{id}", cancellationToken: TestContext.Current.CancellationToken);

        var events = await EventsOf(one.Id);
        Assert.Single(await EventsOf(two.Id), e => e.Type == "headroom.Allocation.created");
        Assert.Equal(["headroom.Allocation.created", "headroom.Allocation.changed", "headroom.Allocation.deleted"], events.Select(e => e.Type));
        Assert.All(events, e => Assert.NotNull(e.Payload));

        var changed = JsonDocument.Parse(events[1].Payload).RootElement;
        Assert.Equal("1.0", changed.GetProperty("specversion").GetString());
        Assert.Equal("headroom.Allocation.changed", changed.GetProperty("type").GetString());
        Assert.Equal(events[1].Id.ToString(), changed.GetProperty("id").GetString());
        var data = changed.GetProperty("data");
        Assert.Equal("Allocation", data.GetProperty("type").GetString());
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal(75, data.GetProperty("percentage").GetInt32());
        Assert.Equal("2026-03-10", data.GetProperty("startDate").GetString());
        Assert.Equal("Flo Good", data.GetProperty("personName").GetString());
        Assert.Equal("wh-flo@example.com", data.GetProperty("personEmail").GetString());
        Assert.Equal("Website", data.GetProperty("projectName").GetString());
        Assert.Equal("Acme Corp", data.GetProperty("clientName").GetString());
        // A delete Event carries the last state.
        Assert.Equal(75, JsonDocument.Parse(events[2].Payload).RootElement.GetProperty("data").GetProperty("percentage").GetInt32());
    }

    [Fact]
    public async Task StoredEventIsSentWithAStandardWebhooksSignature()
    {
        await RemoveAllWebhooks();
        var hook = await AddWebhook();
        await Create(20);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(TestContext.Current.CancellationToken);

        var stored = (await EventsOf(hook.Id)).Single();
        Assert.NotNull(stored.DeliveredAt);
        var sent = _receiver.Requests.First(r => r.Headers["webhook-id"] == stored.Id.ToString());
        Assert.Equal("application/cloudevents+json", sent.ContentType);
        Assert.Equal(stored.Payload, sent.Body);

        // Standard Webhooks reference algorithm: HMAC-SHA256 over "id.timestamp.body" with the base64 key after "whsec_".
        var key = Convert.FromBase64String(hook.Secret["whsec_".Length..]);
        var signed = $"{sent.Headers["webhook-id"]}.{sent.Headers["webhook-timestamp"]}.{sent.Body}";
        var expected = "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
        Assert.Equal(expected, sent.Headers["webhook-signature"]);
    }

    private async Task SendAt(DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var sender = new EventSender(scope.ServiceProvider.GetRequiredService<HeadroomDbContext>(),
            scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(), new FakeTimeProvider(now));
        await sender.SendPendingAsync();
    }

    [Fact]
    public async Task FailedEventIsRetriedAfterTheBackOffWithTheSameWebhookId()
    {
        await RemoveAllWebhooks();
        var hook = await AddWebhook();
        await Create(21);
        var start = DateTimeOffset.UtcNow;
        _receiver.Status = HttpStatusCode.InternalServerError;

        await SendAt(start);
        var failed = (await EventsOf(hook.Id)).Single();
        Assert.Null(failed.DeliveredAt);
        Assert.Contains("500", failed.LastError);

        await SendAt(start.AddSeconds(5)); // Still inside the back-off.
        Assert.Single(_receiver.Requests, r => r.Headers["webhook-id"] == failed.Id.ToString());

        _receiver.Status = HttpStatusCode.OK;
        await SendAt(start.AddMinutes(2));
        var delivered = (await EventsOf(hook.Id)).Single();
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Null(delivered.LastError);
        Assert.Equal(2, _receiver.Requests.Count(r => r.Headers["webhook-id"] == failed.Id.ToString()));
        Assert.Empty(await _planner.GetFromJsonAsync<List<FailedEventResponse>>($"{Url}/failed-events", cancellationToken: TestContext.Current.CancellationToken) ?? []);
    }

    [Fact]
    public async Task EventIsNotRetriedAfter24HoursAndSettingsShowsTheError()
    {
        await RemoveAllWebhooks();
        var hook = await AddWebhook();
        await Create(22);
        var start = DateTimeOffset.UtcNow;
        _receiver.Status = HttpStatusCode.BadGateway;
        await SendAt(start);

        var shown = (await _planner.GetFromJsonAsync<List<FailedEventResponse>>($"{Url}/failed-events", cancellationToken: TestContext.Current.CancellationToken))!.Single(f => f.WebhookId == hook.Id);
        Assert.Contains("502", shown.LastError);
        Assert.False(shown.GaveUp);

        await SendAt(start.AddHours(25));
        Assert.Single(_receiver.Requests, r => r.Headers["webhook-id"] == shown.Id.ToString());
    }

    [Fact]
    public async Task TwoSendersDoNotSendTheSameEventTogether()
    {
        await RemoveAllWebhooks();
        var hook = await AddWebhook();
        await Create(23);
        var now = DateTimeOffset.UtcNow;

        await Task.WhenAll(SendAt(now), SendAt(now));

        var id = (await EventsOf(hook.Id)).Single().Id.ToString();
        Assert.Single(_receiver.Requests, r => r.Headers["webhook-id"] == id);
    }

    [Fact]
    public async Task PlannerRotatesTheSecretAndSeesItOnce()
    {
        var hook = await AddWebhook("Rotate");
        var rotated = await (await _planner.PostAsync($"{Url}/{hook.Id}/secret", null, cancellationToken: TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<CreatedWebhookResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("whsec_", rotated!.Secret);
        Assert.NotEqual(hook.Secret, rotated.Secret);
        Assert.DoesNotContain(rotated.Secret, await _planner.GetStringAsync(Url, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await _planner.PostAsync($"{Url}/{Guid.NewGuid()}/secret", null, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task FailedEventsAreCappedPerWebhookNotInTotal()
    {
        await RemoveAllWebhooks();
        var busy = await AddWebhook("Busy");
        var quiet = await AddWebhook("Quiet");
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        start = start.AddTicks(-start.Ticks % 10); // PostgreSQL stores microseconds
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.Events.AddRange(Enumerable.Range(0, 60).Select(i => Failed(busy.Id, start.AddSeconds(i))));
            db.Events.AddRange(Enumerable.Range(0, 2).Select(i => Failed(quiet.Id, start.AddSeconds(-100 - i))));
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        var shown = (await _planner.GetFromJsonAsync<List<FailedEventResponse>>($"{Url}/failed-events", cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(50, shown.Count(f => f.WebhookId == busy.Id));
        Assert.Equal(2, shown.Count(f => f.WebhookId == quiet.Id));
        Assert.Equal(start.AddSeconds(10), shown.Where(f => f.WebhookId == busy.Id).Min(f => f.DateCreated));
    }

    private static Event Failed(Guid webhookId, DateTimeOffset created) => new()
    {
        WebhookId = webhookId, Type = "headroom.Allocation.created", Payload = "{}", DateCreated = created, Attempts = 1, LastError = "The receiver answered 500.",
    };

    [Theory]
    [InlineData("ftp://receiver.example.com/hook")]
    [InlineData("not a url")]
    public async Task ABadUrlIsAProblemWithADetailText(string url)
    {
        var response = await _planner.PostAsJsonAsync(Url, new WebhookRequest("Bad", url), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("URL", await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABadNameIsAProblemWithADetailText()
    {
        var response = await _planner.PostAsJsonAsync(Url, new WebhookRequest(new string('x', 201), "https://receiver.example.com/hook"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed record Sent(Dictionary<string, string> Headers, string ContentType, string Body);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public ConcurrentQueue<Sent> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(new Sent(
                request.Headers.ToDictionary(h => h.Key, h => h.Value.Single()),
                request.Content!.Headers.ContentType!.MediaType!,
                await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status);
        }
    }
}
