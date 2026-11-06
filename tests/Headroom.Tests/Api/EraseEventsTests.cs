using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Headroom.Tests.Api;

// Erase scrubs the Person from stored Events. The sweep deletes old finished Events.
public class EraseEventsTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private HttpClient _planner = null!;
    private Guid _projectId;

    public EraseEventsTests(HeadroomApp app) =>
        _factory = app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IEmailSender>(_mail);
            s.AddSingleton<TimeProvider>(_time);
        }));

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        // The tests share one database. No old Webhooks or Events keep each test alone.
        await db.Webhooks.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        var ann = new Person { Name = "Ann Planner", Email = $"ee-ann-{_run}@example.com", IsPlanner = true };
        var client = new Headroom.Data.Client { Name = $"Acme Corp {_run}" };
        var project = new Project { ClientId = client.Id, Name = "Website" };
        if (!db.InstanceSettings.Any()) db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.Clients.Add(client);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
        _planner = await TestLogin.SignInAsync(_factory, _mail, $"ee-ann-{_run}@example.com");
        _planner.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_planner));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task EraseBlanksTheNameAndEmailInAllocationAndTimeOffEvents()
    {
        // A Webhook comes first: without a Webhook no Events are stored.
        var hook = await _planner.PostAsJsonAsync("/api/settings/webhooks", new WebhookRequest("Flow", "https://receiver.example.com/hook"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, hook.StatusCode);
        var name = $"Zed Leaver {_run}";
        var email = $"zed.{_run}@example.com";
        var zed = await Run(db => db.People.Add(new Person { Name = name, Email = email, EndDate = new DateOnly(2026, 11, 1) }).Entity.Id);
        await Run(db => db.Contracts.Add(new Contract { PersonId = zed, StartDate = new(2026, 1, 1), HoursMonday = 8 }));
        await _planner.PostAsJsonAsync("/api/allocations", new CreateAllocationRequest(zed, _projectId, new(2026, 5, 4), new(2026, 5, 4), 50), TestContext.Current.CancellationToken);
        await _planner.PostAsJsonAsync($"/api/people/{zed}/time-off", new TimeOffRequest(new(2026, 4, 6), "morning", new(2026, 4, 7), "afternoon"), TestContext.Current.CancellationToken);
        var before = await Payloads();
        Assert.Contains(before, p => p.Contains("headroom.Allocation") && p.Contains(name) && p.Contains(email));
        Assert.Contains(before, p => p.Contains("headroom.TimeOff") && p.Contains(name) && p.Contains(email));

        var response = await _planner.PostAsync($"/api/people/{zed}/erase", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await Payloads();
        Assert.Equal(before.Count, after.Count);
        Assert.All(after, p =>
        {
            Assert.DoesNotContain(name, p);
            Assert.DoesNotContain(email, p);
            Assert.Contains($"\"personId\":\"{zed}\"", p);
            Assert.Contains("Former Person", p);
        });
    }

    [Fact]
    public async Task TheSweepDeletesOldDeliveredAndGivenUpEventsAndKeepsTheRest()
    {
        var now = _time.GetUtcNow();
        var hook = (await Run(db => db.Webhooks.Add(new Webhook { Name = "Flow", Url = "https://receiver.example.com/hook", Secret = EventSender.NewSecret() }).Entity.Id));
        Event Make(string type, TimeSpan age, bool delivered) =>
            new() { WebhookId = hook, Type = type, Payload = "{}", DateCreated = now - age, DeliveredAt = delivered ? now - age : null };
        await Run(db => { db.Events.AddRange(
            Make("old-delivered", TimeSpan.FromDays(31), true),
            Make("old-given-up", TimeSpan.FromDays(31), false),
            Make("young-delivered", TimeSpan.FromDays(29), true),
            Make("young-pending", TimeSpan.FromHours(1), false)); return 0; });

        using (var scope = _factory.Services.CreateScope())
            await EventSender.DeleteExpiredAsync(scope.ServiceProvider.GetRequiredService<HeadroomDbContext>(), now);

        var kept = await Run(db => db.Events.Select(e => e.Type).OrderBy(t => t).ToList());
        Assert.Equal(["young-delivered", "young-pending"], kept);
    }

    [Fact]
    public async Task TheErasureCheckRunsTheSweep()
    {
        var now = _time.GetUtcNow();
        var hook = (await Run(db => db.Webhooks.Add(new Webhook { Name = "Flow", Url = "https://receiver.example.com/hook", Secret = EventSender.NewSecret() }).Entity.Id));
        await Run(db => db.Events.Add(new Event { WebhookId = hook, Type = "old", Payload = "{}", DateCreated = now.AddDays(-31), DeliveredAt = now.AddDays(-31) }));
        _time.Advance(TimeSpan.FromDays(1));

        await _planner.GetAsync("/api/me", TestContext.Current.CancellationToken);

        Assert.Equal(0, await Run(db => db.Events.Count()));
    }

    private Task<List<string>> Payloads() => Run(db => db.Events.AsNoTracking().Select(e => e.Payload).ToList());

    private async Task<T> Run<T>(Func<HeadroomDbContext, T> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var result = work(db);
        await db.SaveChangesAsync();
        return result;
    }
}
