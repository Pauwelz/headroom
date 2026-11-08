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
        if (db.People.Any())
        {
            _seededPeople = db.People.Select(p => p.Id).ToHashSet();
            return;
        }
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
        _seededPeople = [ann.Id, bob.Id, former.Id];
    }

    // The tests that add People remove them again. The other tests expect only Ann, Bob and the erased Person.
    // The Leavers of the end date tests must not show in the other tests either.
    private HashSet<Guid> _seededPeople = [];

    public async ValueTask DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var added = (await db.People.Select(p => p.Id).ToListAsync()).Where(id => !_seededPeople.Contains(id)).ToList();
        await db.Allocations.Where(a => added.Contains(a.PersonId)).ExecuteDeleteAsync();
        await db.Contracts.Where(c => added.Contains(c.PersonId)).ExecuteDeleteAsync();
        await db.People.Where(p => added.Contains(p.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TheServerSpeaksTheCurrentSpecAndListsTheTools()
    {
        var options = ClientOptions();
        options.ProtocolVersion = "2026-07-28";
        await using var mcp = await ConnectAsync(options);

        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["add_company_day_off", "add_contract", "add_person", "add_time_off", "change_allocation", "change_company_day_off", "change_contract", "change_person", "change_project", "change_time_off", "create_allocation", "create_client", "create_project", "delete_allocation", "delete_company_day_off", "delete_contract", "delete_time_off", "find_headroom", "get_end_date_impact", "get_me", "get_schedule", "get_utilisation", "list_clients", "list_company_days_off", "list_people", "list_projects", "remove_end_date", "rename_client", "set_end_date", "split_allocation"], tools.Select(t => t.Name).Order());
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
    public async Task ListPeopleReturnsEveryPersonWithContractsForAReadOnlyPlannerToken()
    {
        await using var mcp = await ConnectAsync();

        var people = (await CallAsync(mcp, "list_people", [])).EnumerateArray().ToList();

        var bob = people.Single(x => x.GetProperty("name").GetString() == "Bob Worker");
        Assert.NotEqual(Guid.Empty, bob.GetProperty("id").GetGuid());
        Assert.Equal("mcp-bob@example.com", bob.GetProperty("email").GetString());
        var contract = bob.GetProperty("contracts").EnumerateArray().Single();
        Assert.NotEqual(Guid.Empty, contract.GetProperty("id").GetGuid());
        Assert.Equal("2026-01-01", contract.GetProperty("startDate").GetString());
        Assert.Contains(people, x => x.GetProperty("name").GetString() == "Ann Planner");
    }

    [Fact]
    public async Task ListPeopleIsRefusedForAPersonWhoIsNoPlanner()
    {
        await using var mcp = await ConnectAsync(secret: WorkerSecret);

        Assert.Contains("not a Planner", await ErrorAsync(mcp, "list_people", []));
    }

    [Fact]
    public async Task CompanyDayOffToolsAddListChangeAndDeleteAndRecordThePersonOfTheToken()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        var year = new Dictionary<string, object?> { ["year"] = 2031 };

        await WriteAsync(mcp, "add_company_day_off", new() { ["date"] = "2031-05-02", ["name"] = "Bridge day" });
        var added = (await CallAsync(mcp, "list_company_days_off", year)).EnumerateArray().Single(d => d.GetProperty("type").GetString() == "CompanyDayOff");
        var id = added.GetProperty("id").GetGuid();
        await WriteAsync(mcp, "change_company_day_off", new() { ["id"] = id, ["date"] = "2031-05-03", ["name"] = "Bridge" });
        var changed = (await CallAsync(mcp, "list_company_days_off", year)).EnumerateArray().Single(d => d.GetProperty("type").GetString() == "CompanyDayOff");
        using var scope = _factory.Services.CreateScope();
        var author = (await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().CompanyDaysOff.SingleAsync(d => d.Id == id, TestContext.Current.CancellationToken)).LastChangedBy;
        await WriteAsync(mcp, "delete_company_day_off", new() { ["id"] = id });

        Assert.Equal(ann, author);
        Assert.Equal(("2031-05-03", "Bridge"), (changed.GetProperty("date").GetString(), changed.GetProperty("name").GetString()));
        Assert.DoesNotContain((await CallAsync(mcp, "list_company_days_off", year)).EnumerateArray(), d => d.GetProperty("type").GetString() == "CompanyDayOff");
    }

    [Fact]
    public async Task ListCompanyDaysOffShowsPublicHolidaysToAReadOnlyPlanner()
    {
        await using var mcp = await ConnectAsync();

        var days = await CallAsync(mcp, "list_company_days_off", new() { ["year"] = 2031 });

        Assert.Contains(days.EnumerateArray(), d => d.GetProperty("type").GetString() == "PublicHoliday");
    }

    [Fact]
    public async Task ASecondCompanyDayOffOnTheSameDateIsRefusedWithTheRestMessage()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        await WriteAsync(mcp, "add_company_day_off", new() { ["date"] = "2032-05-02", ["name"] = "First" });

        var error = await ErrorAsync(mcp, "add_company_day_off", new() { ["date"] = "2032-05-02", ["name"] = "Second" });

        Assert.Contains("This date is already a Company day off.", error);
    }

    [Fact]
    public async Task CompanyDayOffWriteToolsRefuseAReadOnlyTokenAndANonPlanner()
    {
        await using var readOnly = await ConnectAsync();
        await using var worker = await ConnectAsync(secret: WorkerSecret);
        var add = new Dictionary<string, object?> { ["date"] = "2033-05-02", ["name"] = "Nope" };
        var edit = new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["date"] = "2033-05-02", ["name"] = "Nope" };
        var delete = new Dictionary<string, object?> { ["id"] = Guid.NewGuid() };

        foreach (var (tool, args) in new[] { ("add_company_day_off", add), ("change_company_day_off", edit), ("delete_company_day_off", delete) })
        {
            Assert.Contains("read only", await ErrorAsync(readOnly, tool, args));
            Assert.Contains("not a Planner", await ErrorAsync(worker, tool, args));
        }
    }

    [Fact]
    public async Task ListCompanyDaysOffIsRefusedForAPersonWhoIsNoPlanner()
    {
        await using var mcp = await ConnectAsync(secret: WorkerSecret);

        Assert.Contains("not a Planner", await ErrorAsync(mcp, "list_company_days_off", new() { ["year"] = 2031 }));
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
    public async Task SplitAllocationMakesTwoAllocationsThatKeepPersonProjectAndPercentage()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();
        var range = new Dictionary<string, object?> { ["from"] = "2026-08-03", ["to"] = "2026-08-07", ["person"] = "ann" };
        await WriteAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-08-03", ["endDate"] = "2026-08-07", ["percentage"] = 30 });
        var id = Allocations(await CallAsync(mcp, "get_schedule", range)).Single();

        await WriteAsync(mcp, "split_allocation", new() { ["id"] = id, ["date"] = "2026-08-05" });

        Assert.Equal(2, Allocations(await CallAsync(mcp, "get_schedule", range)).Count());
        using var scope = _factory.Services.CreateScope();
        var parts = await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Allocations
            .Where(a => a.PersonId == ann && a.StartDate >= new DateOnly(2026, 8, 3) && a.StartDate <= new DateOnly(2026, 8, 7))
            .OrderBy(a => a.StartDate).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5)], parts.Select(a => a.StartDate));
        Assert.Equal(new DateOnly(2026, 8, 4), parts[0].EndDate);
        Assert.All(parts, a => Assert.Equal((project, 30), (a.ProjectId, a.Percentage)));
    }

    [Fact]
    public async Task SplitAllocationOnTheFirstDayOrOutsideGivesTheMessageOfTheApi()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, project) = await IdsAsync();
        var range = new Dictionary<string, object?> { ["from"] = "2026-09-07", ["to"] = "2026-09-11", ["person"] = "ann" };
        await WriteAsync(mcp, "create_allocation", new() { ["personId"] = ann, ["projectId"] = project, ["startDate"] = "2026-09-07", ["endDate"] = "2026-09-11", ["percentage"] = 30 });
        var id = Allocations(await CallAsync(mcp, "get_schedule", range)).Single();

        var first = await ErrorAsync(mcp, "split_allocation", new() { ["id"] = id, ["date"] = "2026-09-07" });
        var outside = await ErrorAsync(mcp, "split_allocation", new() { ["id"] = id, ["date"] = "2026-09-12" });

        Assert.Contains("split date must be after the start date", first);
        Assert.Contains("split date must be after the start date", outside);
    }

    [Fact]
    public async Task SplitAllocationNeedsCanChangeAndAPlanner()
    {
        await using var readOnly = await ConnectAsync();
        await using var worker = await ConnectAsync(secret: WorkerSecret);
        var args = new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["date"] = "2026-09-09" };

        Assert.Contains("read only", await ErrorAsync(readOnly, "split_allocation", args));
        Assert.Contains("not a Planner", await ErrorAsync(worker, "split_allocation", args));
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
    public async Task AReadOnlyTokenCannotChangeOrDeleteTimeOff()
    {
        await using var mcp = await ConnectAsync();

        var changed = await ErrorAsync(mcp, "change_time_off", new() { ["id"] = Guid.NewGuid(), ["startDate"] = "2026-04-07", ["startPart"] = "morning", ["endDate"] = "2026-04-08", ["endPart"] = "afternoon" });
        var deleted = await ErrorAsync(mcp, "delete_time_off", new() { ["id"] = Guid.NewGuid() });

        Assert.Contains("read only", changed);
        Assert.Contains("read only", deleted);
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

    [Fact]
    public async Task ANonPlannerWithCanChangeAddsChangesAndDeletesTheirOwnTimeOff()
    {
        await using var mcp = await ConnectAsync(secret: WorkerSecret);
        var bob = BobId();
        var range = new Dictionary<string, object?> { ["from"] = "2026-06-01", ["to"] = "2026-06-05", ["person"] = "bob" };

        await WriteAsync(mcp, "add_time_off", new() { ["personId"] = bob, ["startDate"] = "2026-06-02", ["startPart"] = "morning", ["endDate"] = "2026-06-02", ["endPart"] = "afternoon" });
        var id = TimeOffIds(await CallAsync(mcp, "get_schedule", range)).Single();
        await WriteAsync(mcp, "change_time_off", new() { ["id"] = id, ["startDate"] = "2026-06-02", ["startPart"] = "morning", ["endDate"] = "2026-06-03", ["endPart"] = "afternoon" });
        await WriteAsync(mcp, "delete_time_off", new() { ["id"] = id });

        Assert.Empty(TimeOffIds(await CallAsync(mcp, "get_schedule", range)));
    }

    [Fact]
    public async Task ANonPlannerCannotAddOrChangeTimeOffOfAnotherPerson()
    {
        await using var planner = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        await WriteAsync(planner, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-06-08", ["startPart"] = "morning", ["endDate"] = "2026-06-08", ["endPart"] = "afternoon" });
        var id = TimeOffIds(await CallAsync(planner, "get_schedule", new Dictionary<string, object?> { ["from"] = "2026-06-08", ["to"] = "2026-06-12", ["person"] = "ann" })).Single();
        await using var mcp = await ConnectAsync(secret: WorkerSecret);

        var added = await ErrorAsync(mcp, "add_time_off", new() { ["personId"] = ann, ["startDate"] = "2026-06-09", ["startPart"] = "morning", ["endDate"] = "2026-06-09", ["endPart"] = "afternoon" });
        var changed = await ErrorAsync(mcp, "change_time_off", new() { ["id"] = id, ["startDate"] = "2026-06-08", ["startPart"] = "morning", ["endDate"] = "2026-06-09", ["endPart"] = "afternoon" });

        Assert.Contains("You can only change your own Time off.", added);
        Assert.Contains("You can only change your own Time off.", changed);
    }

    [Fact]
    public async Task ANonPlannerWithCanChangeGetsAToolErrorFromEveryAllocationTool()
    {
        await using var mcp = await ConnectAsync(secret: WorkerSecret);
        var (ann, project) = await IdsAsync();
        var dates = new Dictionary<string, object?> { ["startDate"] = "2026-06-01", ["endDate"] = "2026-06-05", ["percentage"] = 40 };

        var create = await ErrorAsync(mcp, "create_allocation", new(dates) { ["personId"] = ann, ["projectId"] = project });
        var change = await ErrorAsync(mcp, "change_allocation", new(dates) { ["id"] = Guid.NewGuid() });

        Assert.Contains("Planner", create);
        Assert.Contains("Planner", change);
    }

    [Fact]
    public async Task EndDateImpactListsWhatTheEndDateCutsForAReadOnlyPlannerToken()
    {
        var leaver = await SeedLeaverAsync("impact");
        await using var mcp = await ConnectAsync();

        var impact = await CallAsync(mcp, "get_end_date_impact", new() { ["personId"] = leaver, ["endDate"] = LeaveDate });

        Assert.Equal(LeaveDate, impact.GetProperty("lastWorkingDay").GetString());
        var records = impact.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(2, records.Count(r => r.GetProperty("kind").GetString() == "Allocation"));
        Assert.Single(records, r => r.GetProperty("deleted").GetBoolean() && r.GetProperty("kind").GetString() == "Allocation");
    }

    [Fact]
    public async Task SetEndDateRemovesLaterAllocationsAndShortensCurrentOnes()
    {
        var leaver = await SeedLeaverAsync("cut");
        await using var mcp = await ConnectAsync(secret: WriterSecret);

        await WriteAsync(mcp, "set_end_date", new() { ["personId"] = leaver, ["endDate"] = LeaveDate });

        var person = (await CallAsync(mcp, "list_people", [])).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == leaver);
        Assert.Equal(LeaveDate, person.GetProperty("endDate").GetString());
        using var scope = _factory.Services.CreateScope();
        var allocations = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Allocations.Where(a => a.PersonId == leaver).ToList();
        Assert.Equal(DateOnly.Parse(LeaveDate), Assert.Single(allocations).EndDate);
    }

    [Fact]
    public async Task EndDateToolsGiveTheMessageOfTheApiWhenARuleIsBroken()
    {
        var leaver = await SeedLeaverAsync("rules");
        var (ann, _) = await IdsAsync();
        await using var mcp = await ConnectAsync(secret: WriterSecret);

        var beforeFirst = await ErrorAsync(mcp, "set_end_date", new() { ["personId"] = leaver, ["endDate"] = "2025-12-31" });
        var lastPlanner = await ErrorAsync(mcp, "set_end_date", new() { ["personId"] = ann, ["endDate"] = LeaveDate });
        var noEndDate = await ErrorAsync(mcp, "remove_end_date", new() { ["personId"] = leaver });

        Assert.Contains("The end date is before the first Contract. Delete the Person instead.", beforeFirst);
        Assert.Contains("Planner", lastPlanner);
        Assert.Contains("This Person has no end date.", noEndDate);
    }

    [Fact]
    public async Task RemoveEndDateRemovesTheEndDate()
    {
        var leaver = await SeedLeaverAsync("remove");
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        await WriteAsync(mcp, "set_end_date", new() { ["personId"] = leaver, ["endDate"] = LeaveDate });

        await WriteAsync(mcp, "remove_end_date", new() { ["personId"] = leaver });

        var person = (await CallAsync(mcp, "list_people", [])).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == leaver);
        Assert.False(person.TryGetProperty("endDate", out var end) && end.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task EndDateWritesNeedAPlannerWithCanChange()
    {
        var leaver = await SeedLeaverAsync("rights");
        await using var readOnly = await ConnectAsync();
        await using var worker = await ConnectAsync(secret: WorkerSecret);
        var set = new Dictionary<string, object?> { ["personId"] = leaver, ["endDate"] = LeaveDate };
        var remove = new Dictionary<string, object?> { ["personId"] = leaver };

        Assert.Contains("read only", await ErrorAsync(readOnly, "set_end_date", set));
        Assert.Contains("read only", await ErrorAsync(readOnly, "remove_end_date", remove));
        Assert.Contains("not a Planner", await ErrorAsync(worker, "set_end_date", set));
        Assert.Contains("not a Planner", await ErrorAsync(worker, "remove_end_date", remove));
        Assert.Contains("not a Planner", await ErrorAsync(worker, "get_end_date_impact", set));
    }

    private const string LeaveDate = "2027-06-30";

    // A Person with a Contract and two Allocations: one that runs past the leave date and one that starts after it.
    private async Task<Guid> SeedLeaverAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var person = new Person { Name = $"Leaver {name}", Email = $"mcp-leaver-{name}@example.com" };
        var project = db.Projects.Single(p => p.Name == "Rollout");
        db.People.Add(person);
        await db.SaveChangesAsync(); // The Events of the Allocations look up the Person in the database.
        db.Contracts.Add(new Contract { PersonId = person.Id, StartDate = new(2026, 1, 1), HoursMonday = 8, HoursTuesday = 8 });
        db.Allocations.Add(new Allocation { PersonId = person.Id, ProjectId = project.Id, StartDate = new(2027, 6, 21), EndDate = new(2027, 7, 10), Percentage = 50 });
        db.Allocations.Add(new Allocation { PersonId = person.Id, ProjectId = project.Id, StartDate = new(2027, 7, 12), EndDate = new(2027, 7, 16), Percentage = 50 });
        await db.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task ListClientsWorksWithAReadOnlyTokenAndShowsTheProjectsOfEachClient()
    {
        await using var mcp = await ConnectAsync();

        var clients = await CallAsync(mcp, "list_clients", []);

        var globex = clients.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Globex");
        Assert.Equal("Rollout", Assert.Single(globex.GetProperty("projects").EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task ClientToolsCreateAndRenameAndStoreEventsWithThePersonOfTheToken()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        try
        {
            await WriteAsync(mcp, "create_client", new() { ["name"] = "Initech-mcp" });
            var id = await ClientIdAsync(mcp, "Initech-mcp");
            await WriteAsync(mcp, "rename_client", new() { ["id"] = id, ["name"] = "Initrode-mcp" });

            Assert.Equal(id, await ClientIdAsync(mcp, "Initrode-mcp"));
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            Assert.Equal(ann, (await db.Clients.SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken)).LastChangedBy);
        }
        finally { await RemoveClientsAsync("Initech-mcp", "Initrode-mcp"); }
    }

    [Fact]
    public async Task ClientToolsRefuseADuplicateNameWithTheMessageOfTheApi()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);

        try
        {
            await WriteAsync(mcp, "create_client", new() { ["name"] = "Umbrella-mcp" });
            var id = await ClientIdAsync(mcp, "Umbrella-mcp");

            var created = await ErrorAsync(mcp, "create_client", new() { ["name"] = "globex" });
            var renamed = await ErrorAsync(mcp, "rename_client", new() { ["id"] = id, ["name"] = "GLOBEX" });

            Assert.Contains("A Client with this name exists.", created);
            Assert.Contains("A Client with this name exists.", renamed);
        }
        finally { await RemoveClientsAsync("Umbrella-mcp"); }
    }

    [Fact]
    public async Task ProjectToolsCreateAndChangeAProjectAndRefuseWhatTheApiRefuses()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        var (ann, _) = await IdsAsync();
        try
        {
            await WriteAsync(mcp, "create_client", new() { ["name"] = "Hooli-mcp" });
            var client = await ClientIdAsync(mcp, "Hooli-mcp");
            await WriteAsync(mcp, "create_project", new() { ["clientId"] = client, ["name"] = "Phase 1", ["status"] = "tentative" });
            var project = await ProjectIdAsync(mcp, "Phase 1");

            Assert.Contains("A new Project is Tentative or Confirmed.", await ErrorAsync(mcp, "create_project", new() { ["clientId"] = client, ["name"] = "Phase 2", ["status"] = "archived" }));
            Assert.Contains("This Client has a Project with this name.", await ErrorAsync(mcp, "create_project", new() { ["clientId"] = client, ["name"] = "Phase 1", ["status"] = "confirmed" }));

            await WriteAsync(mcp, "change_project", new() { ["id"] = project, ["name"] = "Phase 1b", ["clientId"] = client, ["status"] = "archived" });
            Assert.Contains("An Archived Project cannot become Tentative", await ErrorAsync(mcp, "change_project", new() { ["id"] = project, ["name"] = "Phase 1b", ["clientId"] = client, ["status"] = "tentative" }));

            var shown = (await CallAsync(mcp, "list_projects", new() { ["status"] = "archived" })).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == project);
            Assert.Equal("Phase 1b", shown.GetProperty("name").GetString());
            Assert.Equal(ann, shown.GetProperty("lastChangedBy").GetGuid());
        }
        finally { await RemoveClientsAsync("Hooli-mcp"); }
    }

    [Fact]
    public async Task ClientAndProjectWriteToolsNeedAPlannerWithCanChange()
    {
        await using var readOnly = await ConnectAsync();
        await using var worker = await ConnectAsync(secret: WorkerSecret);
        var id = Guid.NewGuid();
        var calls = new (string Tool, Dictionary<string, object?> Args)[]
        {
            ("create_client", new() { ["name"] = "X" }),
            ("rename_client", new() { ["id"] = id, ["name"] = "X" }),
            ("create_project", new() { ["clientId"] = id, ["name"] = "X", ["status"] = "tentative" }),
            ("change_project", new() { ["id"] = id, ["name"] = "X", ["clientId"] = id, ["status"] = "tentative" }),
        };

        foreach (var (tool, args) in calls)
        {
            Assert.Contains("not a Planner", await ErrorAsync(worker, tool, args));
            Assert.Contains("read only", await ErrorAsync(readOnly, tool, args));
        }
    }

    private static async Task<Guid> ClientIdAsync(McpClient mcp, string name) =>
        (await CallAsync(mcp, "list_clients", [])).EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static async Task<Guid> ProjectIdAsync(McpClient mcp, string name) =>
        (await CallAsync(mcp, "list_projects", [])).EnumerateArray().Single(p => p.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    // Other tests count the Projects of the fixture, so a test removes what it made.
    private async Task RemoveClientsAsync(params string[] names)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var clients = await db.Clients.Where(c => names.Contains(c.Name)).ToListAsync(TestContext.Current.CancellationToken);
        var ids = clients.Select(c => c.Id).ToList();
        db.Projects.RemoveRange(db.Projects.Where(p => ids.Contains(p.ClientId)));
        db.Clients.RemoveRange(clients);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }


    private static readonly object FullWeek = new Dictionary<string, object?> { ["monday"] = 8, ["tuesday"] = 8, ["wednesday"] = 8, ["thursday"] = 8, ["friday"] = 0, ["saturday"] = 0, ["sunday"] = 0 };

    private static Dictionary<string, object?> NewPerson(string email, object? hours = null) =>
        new() { ["name"] = "Eva", ["email"] = email, ["isPlanner"] = false, ["startDate"] = "2026-03-01", ["hours"] = hours ?? FullWeek };

    private static async Task<JsonElement> PersonAsync(McpClient mcp, string email) =>
        (await CallAsync(mcp, "list_people", [])).EnumerateArray().Single(x => x.TryGetProperty("email", out var e) && e.GetString() == email);

    [Fact]
    public async Task APlannerAddsAPersonWithAFirstContractAndTheChangeNamesThePlanner()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);

        await WriteAsync(mcp, "add_person", NewPerson("mcp-eva@example.com"));

        var eva = await PersonAsync(mcp, "mcp-eva@example.com");
        var contract = eva.GetProperty("contracts").EnumerateArray().Single();
        Assert.Equal("2026-03-01", contract.GetProperty("startDate").GetString());
        Assert.Equal(32m, contract.GetProperty("hoursPerWeek").GetDecimal());
        var (ann, _) = await IdsAsync();
        Assert.Equal(ann, eva.GetProperty("lastChangedBy").GetGuid());
    }

    [Fact]
    public async Task AddPersonRefusesABadEmailADuplicateEmailAndBadHoursWithTheRestMessage()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);

        Assert.Contains("Enter a valid email address.", await ErrorAsync(mcp, "add_person", NewPerson("nope")));
        Assert.Contains("Another Person already has this email.", await ErrorAsync(mcp, "add_person", NewPerson("mcp-bob@example.com")));
        var bad = new Dictionary<string, object?> { ["monday"] = 24.1, ["tuesday"] = 0, ["wednesday"] = 0, ["thursday"] = 0, ["friday"] = 0, ["saturday"] = 0, ["sunday"] = 0 };
        Assert.Contains("Hours are 0 to 24 per day", await ErrorAsync(mcp, "add_person", NewPerson("mcp-bad@example.com", bad)));
    }

    [Fact]
    public async Task ChangePersonChangesNameEmailAndRoleAndRefusesTheLastPlannerAndAnErasedPerson()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        await WriteAsync(mcp, "add_person", NewPerson("mcp-chg@example.com"));
        var id = (await PersonAsync(mcp, "mcp-chg@example.com")).GetProperty("id").GetGuid();

        await WriteAsync(mcp, "change_person", new() { ["id"] = id, ["name"] = "Eva Two", ["email"] = "mcp-chg2@example.com", ["isPlanner"] = true });
        var changed = await PersonAsync(mcp, "mcp-chg2@example.com");

        Assert.Equal("Eva Two", changed.GetProperty("name").GetString());
        Assert.True(changed.GetProperty("isPlanner").GetBoolean());
        // Eva is a second Planner now and can lose the flag again. Ann is the last Planner then.
        await WriteAsync(mcp, "change_person", new() { ["id"] = id, ["name"] = "Eva Two", ["email"] = "mcp-chg2@example.com", ["isPlanner"] = false });
        var (ann, _) = await IdsAsync();
        Assert.Contains("last Planner", await ErrorAsync(mcp, "change_person", new() { ["id"] = ann, ["name"] = "Ann Planner", ["email"] = "mcp-ann@example.com", ["isPlanner"] = false }));
        var erased = (await CallAsync(mcp, "list_people", [])).EnumerateArray().Single(x => x.TryGetProperty("erasedNumber", out var n) && n.ValueKind == JsonValueKind.Number).GetProperty("id").GetGuid();
        Assert.Contains("erased Person", await ErrorAsync(mcp, "change_person", new() { ["id"] = erased, ["name"] = "X", ["email"] = "x@example.com", ["isPlanner"] = false }));
    }

    [Fact]
    public async Task ContractToolsAddChangeAndDeleteAndRefuseTheLastContractAndADuplicateStartDate()
    {
        await using var mcp = await ConnectAsync(secret: WriterSecret);
        await WriteAsync(mcp, "add_person", NewPerson("mcp-con@example.com"));
        var eva = await PersonAsync(mcp, "mcp-con@example.com");
        var person = eva.GetProperty("id").GetGuid();
        var first = eva.GetProperty("contracts")[0].GetProperty("id").GetGuid();
        Assert.Contains("at least one Contract", await ErrorAsync(mcp, "delete_contract", new() { ["personId"] = person, ["contractId"] = first }));

        await WriteAsync(mcp, "add_contract", new() { ["personId"] = person, ["startDate"] = "2026-06-01", ["hours"] = FullWeek });
        Assert.Contains("already has a Contract", await ErrorAsync(mcp, "add_contract", new() { ["personId"] = person, ["startDate"] = "2026-06-01", ["hours"] = FullWeek }));
        var second = (await PersonAsync(mcp, "mcp-con@example.com")).GetProperty("contracts")[1].GetProperty("id").GetGuid();
        await WriteAsync(mcp, "change_contract", new() { ["personId"] = person, ["contractId"] = second, ["startDate"] = "2026-07-01", ["hours"] = FullWeek });
        Assert.Equal("2026-07-01", (await PersonAsync(mcp, "mcp-con@example.com")).GetProperty("contracts")[1].GetProperty("startDate").GetString());
        await WriteAsync(mcp, "delete_contract", new() { ["personId"] = person, ["contractId"] = second });

        Assert.Single((await PersonAsync(mcp, "mcp-con@example.com")).GetProperty("contracts").EnumerateArray());
    }

    [Fact]
    public async Task EveryPeopleWriteToolRefusesAReadOnlyPlannerTokenAndAPersonWhoIsNoPlanner()
    {
        var id = Guid.NewGuid();
        var calls = new Dictionary<string, Dictionary<string, object?>>
        {
            ["add_person"] = NewPerson("mcp-no@example.com"),
            ["change_person"] = new() { ["id"] = id, ["name"] = "X", ["email"] = "x@example.com", ["isPlanner"] = false },
            ["add_contract"] = new() { ["personId"] = id, ["startDate"] = "2026-06-01", ["hours"] = FullWeek },
            ["change_contract"] = new() { ["personId"] = id, ["contractId"] = id, ["startDate"] = "2026-06-01", ["hours"] = FullWeek },
            ["delete_contract"] = new() { ["personId"] = id, ["contractId"] = id },
        };
        await using var readOnly = await ConnectAsync();
        await using var worker = await ConnectAsync(secret: WorkerSecret);

        foreach (var (tool, args) in calls)
        {
            Assert.Contains("read only", await ErrorAsync(readOnly, tool, args));
            Assert.Contains("not a Planner", await ErrorAsync(worker, tool, args));
        }
    }

    private Guid BobId()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Single(p => p.Email == "mcp-bob@example.com").Id;
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
