using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Headroom.Auth;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Headroom.Tests.Api;

public class McpTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Secret = "hr_mcp-test-secret";
    private const string WriterSecret = "hr_mcp-writer-secret";
    private const string WorkerSecret = "hr_mcp-worker-secret";
    private readonly WebApplicationFactory<Program> _factory;

    private readonly FakeEmailSender _mail;

    public McpTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann Planner", Email = "mcp-ann@example.com", IsPlanner = true };
        var bob = new Person { Name = "Bob Worker", Email = "mcp-bob@example.com" };
        var client = new Headroom.Data.Client { Name = "Globex" };
        var project = new Project { ClientId = client.Id, Name = "Rollout", Status = ProjectStatus.Confirmed };
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        var former = new Person { Name = "Former Person 1", Email = null, ErasedNumber = 1 };
        db.People.AddRange(ann, bob, former);
        db.Clients.Add(client);
        db.Projects.Add(project);
        foreach (var p in new[] { ann, bob, former })
            db.Contracts.Add(new Contract
            {
                PersonId = p.Id, StartDate = new(2026, 1, 1),
                HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
            });
        db.Allocations.Add(new Allocation { PersonId = bob.Id, ProjectId = project.Id, StartDate = new(2026, 3, 2), EndDate = new(2026, 3, 6), Percentage = 50 });
        db.AccessTokens.Add(new AccessToken { Name = "Claude", PersonId = ann.Id, TokenHash = AccessTokenAuth.Hash(Secret), LastChangedBy = ann.Id });
        db.AccessTokens.Add(new AccessToken { Name = "Writer", PersonId = ann.Id, CanChange = true, TokenHash = AccessTokenAuth.Hash(WriterSecret), LastChangedBy = ann.Id });
        // Bob is no Planner, so his token with Can change writes only his own Time off.
        db.AccessTokens.Add(new AccessToken { Name = "Worker", PersonId = bob.Id, CanChange = true, TokenHash = AccessTokenAuth.Hash(WorkerSecret), LastChangedBy = bob.Id });
        db.Webhooks.Add(new Webhook { Name = "Hook", Url = "https://hooks.example.com/a", Secret = "whsec_x", LastChangedBy = ann.Id });
        db.Webhooks.Add(new Webhook { Name = "Hook 2", Url = "https://hooks.example.com/b", Secret = "whsec_y", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TheServerSpeaksTheCurrentSpecAndListsTheTools()
    {
        var options = ClientOptions();
        options.ProtocolVersion = "2026-07-28";
        await using var mcp = await ConnectAsync(options);

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["add_time_off", "change_allocation", "change_time_off", "create_allocation", "delete_allocation", "delete_time_off", "find_headroom", "get_me", "get_schedule", "get_utilisation", "list_projects"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task GetMeReturnsThePersonOfTheToken()
    {
        await using var mcp = await ConnectAsync();

        var me = await CallAsync(mcp, "get_me", []);

        Assert.Equal(("Ann Planner", true), (me.GetProperty("name").GetString(), me.GetProperty("isPlanner").GetBoolean()));
        Assert.NotEqual(Guid.Empty, me.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetScheduleReturnsTheScheduleAndFiltersOnPerson()
    {
        await using var mcp = await ConnectAsync();

        var all = await CallAsync(mcp, "get_schedule", new() { ["from"] = "2026-03-02", ["to"] = "2026-03-08" });
        var one = await CallAsync(mcp, "get_schedule", new() { ["from"] = "2026-03-02", ["to"] = "2026-03-08", ["person"] = "bob" });

        Assert.Equal("Schedule", all.GetProperty("type").GetString());
        Assert.Equal(["Ann Planner", "Bob Worker"], Names(all));
        Assert.Equal(["Bob Worker"], Names(one));
    }

    [Fact]
    public async Task GetUtilisationReturnsTheReport()
    {
        await using var mcp = await ConnectAsync();

        var report = await CallAsync(mcp, "get_utilisation", new() { ["from"] = "2026-03-02", ["to"] = "2026-03-08" });

        Assert.Equal("Utilisation", report.GetProperty("type").GetString());
    }

    [Fact]
    public async Task FindHeadroomReturnsPeopleWithFreeHours()
    {
        await using var mcp = await ConnectAsync();

        var found = await CallAsync(mcp, "find_headroom", new() { ["from"] = "2026-03-02", ["to"] = "2026-03-06", ["hours"] = 30 });

        // Ann has 40 free hours. Bob has 20 free hours.
        Assert.Equal(["Ann Planner"], found.EnumerateArray().Select(x => x.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task FindHeadroomOnlyCountsTheDaysInsideARangeThatStartsMidMonth()
    {
        await using var mcp = await ConnectAsync();

        // Tuesday and Wednesday only: 16 h Capacity each. Bob has 50% on those days, so 8 h allocated.
        var found = await CallAsync(mcp, "find_headroom", new() { ["from"] = "2026-03-03", ["to"] = "2026-03-04" });

        Assert.DoesNotContain(found.EnumerateArray(), x => x.GetProperty("name").GetString()!.StartsWith("Former Person"));
        var bob = found.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Bob Worker");
        Assert.Equal((16m, 8m, 8m), (bob.GetProperty("capacity").GetDecimal(), bob.GetProperty("allocatedHours").GetDecimal(), bob.GetProperty("freeHours").GetDecimal()));
    }

    [Fact]
    public async Task McpDoesNotAcceptTheLoginCookie()
    {
        var planner = await TestLogin.SignInAsync(_factory, _mail, "mcp-ann@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, (await planner.PostAsync("/mcp", Json(), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ListProjectsReturnsProjectsAndFiltersOnStatus()
    {
        await using var mcp = await ConnectAsync();

        var all = await CallAsync(mcp, "list_projects", []);
        var archived = await CallAsync(mcp, "list_projects", new() { ["status"] = "archived" });

        var project = Assert.Single(all.EnumerateArray());
        Assert.Equal("Rollout", project.GetProperty("name").GetString());
        Assert.Equal("confirmed", project.GetProperty("status").GetString());
        Assert.Equal(0, archived.GetArrayLength());
    }

    [Fact]
    public async Task ABadRangeIsAToolError()
    {
        await using var mcp = await ConnectAsync();

        var result = await mcp.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["from"] = "2026-03-08", ["to"] = "2026-03-02" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task McpNeedsAnAccessToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync("/mcp", Json(), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http("hr_nope").PostAsync("/mcp", Json(), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task McpRejectsAnOtherOriginAndAcceptsTheSameOne()
    {
        var http = Http(Secret);
        var foreign = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json() };
        foreign.Headers.Add("Origin", "https://evil.example");
        var same = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json() };
        same.Headers.Add("Origin", "https://localhost");

        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(foreign, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await http.SendAsync(same, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AllocationToolsCreateChangeAndDeleteAndShowInTheScheduleAndEvents()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();
        var range = new Dictionary<string, object?> { ["from"] = "2026-04-06", ["to"] = "2026-04-10", ["person"] = "ann" };

        await WriteAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-04-06", ["endDate"] = "2026-04-10", ["percentage"] = 40 });
        var created = Allocations(await CallAsync(mcp, "get_schedule", range)).Single();
        await WriteAsync(mcp, "change_allocation", new() { ["id"] = created, ["startDate"] = "2026-04-06", ["endDate"] = "2026-04-08", ["percentage"] = 60 });
        var changed = Allocations(await CallAsync(mcp, "get_schedule", range)).Single();
        await WriteAsync(mcp, "delete_allocation", new() { ["id"] = created });

        Assert.Equal(created, changed);
        Assert.Empty(Allocations(await CallAsync(mcp, "get_schedule", range)));
        using var scope = _factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events.Select(e => e.Type).ToList();
        Assert.Contains("headroom.Allocation.created", types);
        Assert.Contains("headroom.Allocation.deleted", types);
    }

    [Fact]
    public async Task AWriteToolRecordsThePersonOfTheTokenAndMakesOneEventPerWebhook()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();

        await WriteAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-06-01", ["endDate"] = "2026-06-05", ["percentage"] = 20 });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var allocation = await db.Allocations.SingleAsync(a => a.PersonId == ann && a.StartDate == new DateOnly(2026, 6, 1), TestContext.Current.CancellationToken);
        Assert.Equal(ann, allocation.LastChangedBy);
        var payloads = (await db.Events.Select(e => e.Payload).ToListAsync(TestContext.Current.CancellationToken))
            .Where(p => p.Contains("headroom.Allocation.created") && p.Contains(allocation.Id.ToString())).ToList();
        Assert.Equal(await db.Webhooks.CountAsync(TestContext.Current.CancellationToken), payloads.Count);
        Assert.All(payloads, p => Assert.Contains(ann.ToString(), p));
    }

    [Fact]
    public async Task ATokenThatCanChangeFailsOnItsNextWriteWhenThePersonLosesThePlannerRole()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();
        var args = new Dictionary<string, object?> { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-07-06", ["endDate"] = "2026-07-10", ["percentage"] = 20 };
        await WriteAsync(mcp, "create_allocation", args);

        await SetPlannerAsync(ann, false);
        try { Assert.Contains("not a Planner", await ErrorAsync(mcp, "create_allocation", args)); }
        finally { await SetPlannerAsync(ann, true); }
    }

    [Fact]
    public async Task AllocationToolsGiveTheMessageOfTheApiWhenARuleIsBroken()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();

        var error = await ErrorAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-04-06", ["endDate"] = "2026-04-10", ["percentage"] = 42 });

        Assert.Contains("5 to 100 in steps of 5", error);
    }

    [Fact]
    public async Task AReadOnlyTokenCannotWrite()
    {
        await using var mcp = await ConnectAsync();
        var (ann, project) = await IdsAsync();

        var error = await ErrorAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-04-06", ["endDate"] = "2026-04-10", ["percentage"] = 40 });

        Assert.Contains("read only", error);
    }

    [Fact]
    public async Task ATokenOfAPersonWhoIsNoPlannerCannotWrite()
    {
        await using var mcp = await ConnectAsync(secret: WorkerSecret);

        var error = await ErrorAsync(mcp, "delete_allocation", new() { ["id"] = Guid.NewGuid() });

        Assert.Contains("Planner", error);
    }

    [Fact]
    public async Task TimeOffToolsAddChangeAndDeleteAndShowInTheScheduleAndEvents()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        var range = new Dictionary<string, object?> { ["from"] = "2026-04-06", ["to"] = "2026-04-10", ["person"] = "ann" };

        await WriteAsync(mcp, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-04-07", ["startPart"] = "morning", ["endDate"] = "2026-04-08", ["endPart"] = "afternoon" });
        var added = TimeOffIds(await CallAsync(mcp, "get_schedule", range)).Single();
        await WriteAsync(mcp, "change_time_off", new() { ["id"] = added, ["startDate"] = "2026-04-07", ["startPart"] = "afternoon", ["endDate"] = "2026-04-09", ["endPart"] = "afternoon" });
        var changed = TimeOffIds(await CallAsync(mcp, "get_schedule", range)).Single();
        await WriteAsync(mcp, "delete_time_off", new() { ["id"] = added });

        Assert.Equal(added, changed);
        Assert.Empty(TimeOffIds(await CallAsync(mcp, "get_schedule", range)));
        using var scope = _factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events.Select(e => e.Type).ToList();
        Assert.Contains("headroom.TimeOff.created", types);
        Assert.Contains("headroom.TimeOff.deleted", types);
    }

    [Fact]
    public async Task TimeOffToolsGiveTheMessageOfTheApiWhenARuleIsBroken()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();

        var error = await ErrorAsync(mcp, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-04-08", ["startPart"] = "morning", ["endDate"] = "2026-04-07", ["endPart"] = "afternoon" });

        Assert.Contains("Time off must end after it starts", error);
    }

    [Fact]
    public async Task AReadOnlyTokenCannotWriteTimeOff()
    {
        await using var mcp = await ConnectAsync();
        var (ann, _) = await IdsAsync();

        var error = await ErrorAsync(mcp, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-04-07", ["startPart"] = "morning", ["endDate"] = "2026-04-08", ["endPart"] = "afternoon" });

        Assert.Contains("read only", error);
    }

    [Fact]
    public async Task ATokenOfAPersonWhoIsNoPlannerCannotDeleteTimeOffOfAnotherPerson()
    {
        await using var planner = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        await WriteAsync(planner, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-05-04", ["startPart"] = "morning", ["endDate"] = "2026-05-04", ["endPart"] = "afternoon" });
        var id = TimeOffIds(await CallAsync(planner, "get_schedule", new Dictionary<string, object?> { ["from"] = "2026-05-04", ["to"] = "2026-05-08", ["person"] = "ann" })).Single();
        await using var mcp = await ConnectAsync(secret: WorkerSecret);

        var error = await ErrorAsync(mcp, "delete_time_off", new() { ["id"] = id });

        Assert.Contains("You can only change your own Time off.", error);
    }

    private static List<Guid> TimeOffIds(JsonElement schedule) => schedule.GetProperty("people").EnumerateArray()
        .SelectMany(p => p.GetProperty("timeOff").EnumerateArray()).Select(t => t.GetProperty("id").GetGuid()).ToList();

    private async Task SetPlannerAsync(Guid id, bool isPlanner)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People
            .Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, isPlanner), TestContext.Current.CancellationToken);
    }

    private async Task<(Guid Ann, Guid Project)> IdsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        return (db.People.Single(p => p.Email == "mcp-ann@example.com").Id, db.Projects.Single(p => p.Name == "Rollout").Id);
    }

    private static List<Guid> Allocations(JsonElement schedule) => schedule.GetProperty("people").EnumerateArray()
        .SelectMany(p => p.GetProperty("projects").EnumerateArray()).SelectMany(p => p.GetProperty("allocations").EnumerateArray())
        .Select(a => a.GetProperty("id").GetGuid()).ToList();

    private static async Task WriteAsync(McpClient mcp, string tool, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(tool, args);
        Assert.True(result.IsError != true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    private static async Task<string> ErrorAsync(McpClient mcp, string tool, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(tool, args);
        Assert.True(result.IsError);
        return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    private static IEnumerable<string?> Names(JsonElement schedule) =>
        schedule.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("name").GetString());

    private HttpClient Http(string secret)
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return http;
    }

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    private Task<McpClient> ConnectAsync(McpClientOptions? options = null, string secret = Secret)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp") }, Http(secret), ownsHttpClient: true);
        return McpClient.CreateAsync(transport, options ?? ClientOptions());
    }

    // The SDK's discover probe gives up after a short timeout and falls back to the old handshake.
    // On a slow CI runner the first call can take longer, so give the probe time.
    internal static McpClientOptions ClientOptions() => new() { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) };

    private static async Task<JsonElement> CallAsync(McpClient mcp, string tool, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(tool, args);
        Assert.True(result.IsError != true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        return JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
    }
}
