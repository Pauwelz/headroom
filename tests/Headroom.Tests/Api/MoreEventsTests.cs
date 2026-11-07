using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Events for Time off and Project status. The Allocation Events have their tests in WebhookEventsTests.
public class MoreEventsTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private HttpClient _planner = null!;
    private Guid _floId;
    private Guid _clientId;
    private Guid _projectId;

    public MoreEventsTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            // The tests share one database and run one after another. Fresh records and no old Webhooks keep each test alone.
            await db.Webhooks.ExecuteDeleteAsync();
            var ann = new Person { Name = "Ann Planner", Email = $"ev-ann-{_run}@example.com", IsPlanner = true };
            var flo = new Person { Name = "Flo Good", Email = $"ev-flo-{_run}@example.com" };
            var client = new Headroom.Data.Client { Name = $"Acme Corp {_run}" };
            var project = new Project { ClientId = client.Id, Name = "Website" };
            if (!db.InstanceSettings.Any()) db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.AddRange(ann, flo);
            db.Clients.Add(client);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            _floId = flo.Id;
            _clientId = client.Id;
            _projectId = project.Id;
        }
        _planner = await TestLogin.SignInAsync(_factory, _mail, $"ev-ann-{_run}@example.com");
        _planner.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_planner));
        var added = await _planner.PostAsJsonAsync("/api/settings/webhooks", new WebhookRequest("Flow", "https://receiver.example.com/hook"));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<List<(string Type, JsonElement Data)>> StoredEvents()
    {
        using var scope = _factory.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events.OrderBy(e => e.DateCreated).ToListAsync();
        return events.Select(e => (e.Type, JsonDocument.Parse(e.Payload).RootElement.GetProperty("data").Clone())).ToList();
    }

    private Task<HttpResponseMessage> PutProject(string name, string status) =>
        _planner.PutAsJsonAsync($"/api/projects/{_projectId}", new UpdateProjectRequest(name, _clientId, status));

    [Fact]
    public async Task TimeOffCreateChangeAndDeleteStoreEvents()
    {
        var body = new TimeOffRequest(new(2026, 4, 6), "morning", new(2026, 4, 7), "afternoon");
        var created = await _planner.PostAsJsonAsync($"/api/people/{_floId}/time-off", body, cancellationToken: TestContext.Current.CancellationToken);
        var id = (await created.Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken))!.Id;
        await _planner.PutAsJsonAsync($"/api/time-off/{id}", body with { EndDate = new(2026, 4, 8) }, cancellationToken: TestContext.Current.CancellationToken);
        await _planner.DeleteAsync($"/api/time-off/{id}", cancellationToken: TestContext.Current.CancellationToken);

        var events = await StoredEvents();
        Assert.Equal(["headroom.TimeOff.created", "headroom.TimeOff.changed", "headroom.TimeOff.deleted"], events.Select(e => e.Type));
        var changed = events[1].Data;
        Assert.Equal("TimeOff", changed.GetProperty("type").GetString());
        Assert.Equal(id, changed.GetProperty("id").GetGuid());
        Assert.Equal("Flo Good", changed.GetProperty("personName").GetString());
        Assert.Equal($"ev-flo-{_run}@example.com", changed.GetProperty("personEmail").GetString());
        Assert.Equal("2026-04-08", changed.GetProperty("endDate").GetString());
        Assert.Equal("morning", changed.GetProperty("startPart").GetString());
        Assert.Equal("2026-04-08", events[2].Data.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task ATimeOffWriteByANonPlannerStoresTheSameEventAsAPlannerWriteAndSetsLastChangedBy()
    {
        var body = new TimeOffRequest(new(2026, 4, 6), "morning", new(2026, 4, 7), "afternoon");
        await _planner.PostAsJsonAsync($"/api/people/{_floId}/time-off", body, cancellationToken: TestContext.Current.CancellationToken);
        var flo = await TestLogin.SignInAsync(_factory, _mail, $"ev-flo-{_run}@example.com");
        flo.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(flo));

        var created = await flo.PostAsJsonAsync($"/api/people/{_floId}/time-off", body with { StartDate = new(2026, 5, 4), EndDate = new(2026, 5, 5) }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var events = await StoredEvents();
        Assert.Equal(["headroom.TimeOff.created", "headroom.TimeOff.created"], events.Select(e => e.Type));
        Assert.Equal(events[0].Data.EnumerateObject().Select(p => p.Name), events[1].Data.EnumerateObject().Select(p => p.Name));
        var id = (await created.Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken))!.Id;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        Assert.Equal(_floId, (await db.TimeOff.SingleAsync(t => t.Id == id, TestContext.Current.CancellationToken)).LastChangedBy);
    }

    [Fact]
    public async Task OnlyAProjectStatusChangeStoresAProjectEvent()
    {
        Assert.Equal(HttpStatusCode.OK, (await PutProject("Website 2", "tentative")).StatusCode);
        Assert.Empty(await StoredEvents());

        Assert.Equal(HttpStatusCode.OK, (await PutProject("Website 2", "confirmed")).StatusCode);
        var (type, data) = Assert.Single(await StoredEvents());
        Assert.Equal("headroom.Project.changed", type);
        Assert.Equal("Project", data.GetProperty("type").GetString());
        Assert.Equal("Website 2", data.GetProperty("name").GetString());
        Assert.Equal("confirmed", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DeletingAProjectStoresADeletedEventForEachAllocation()
    {
        foreach (var day in new[] { 2, 3 })
            await _planner.PostAsJsonAsync("/api/allocations", new CreateAllocationRequest(_floId, _projectId, new(2026, 5, day), new(2026, 5, day), 50), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, (await _planner.DeleteAsync($"/api/projects/{_projectId}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        var deleted = (await StoredEvents()).Where(e => e.Type == "headroom.Allocation.deleted").ToList();
        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, e => Assert.Equal("Website", e.Data.GetProperty("projectName").GetString()));
    }
}
