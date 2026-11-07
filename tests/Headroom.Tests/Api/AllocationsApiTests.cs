using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class AllocationsApiTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _planner = null!;
    private HttpClient _viewer = null!;
    private Guid _annId;
    private Guid _floId;
    private Guid _leaverId;

    public AllocationsApiTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            if (!db.People.Any())
            {
                var ann = new Person { Name = "Ann Planner", Email = "alloc-ann@example.com", IsPlanner = true };
                var flo = new Person { Name = "Flo Good", Email = "alloc-flo@example.com" };
                var leaver = new Person { Name = "Lee Ver", Email = "alloc-lee@example.com", EndDate = March(20) };
                db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
                db.People.AddRange(ann, flo, leaver);
                await db.SaveChangesAsync();
            }
            _annId = db.People.Single(p => p.Email == "alloc-ann@example.com").Id;
            _floId = db.People.Single(p => p.Email == "alloc-flo@example.com").Id;
            _leaverId = db.People.Single(p => p.Email == "alloc-lee@example.com").Id;
        }
        _planner = await TestLogin.SignInAsync(_factory, _mail, "alloc-ann@example.com");
        _planner.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_planner));
        _viewer = await TestLogin.SignInAsync(_factory, _mail, "alloc-flo@example.com");
        _viewer.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_viewer));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<ProjectResponse> NewProject(string status = "confirmed")
    {
        var client = (await (await _planner.PostAsJsonAsync("/api/clients", new ClientRequest(Unique("Client"))))
            .Content.ReadFromJsonAsync<ClientResponse>())!;
        var response = await _planner.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new CreateProjectRequest(Unique("Project"), status));
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private Task<HttpResponseMessage> Create(Guid personId, Guid projectId, DateOnly start, DateOnly end, int percentage = 50) =>
        _planner.PostAsJsonAsync("/api/allocations", new CreateAllocationRequest(personId, projectId, start, end, percentage));

    private async Task<List<ScheduleAllocation>> Bars(Guid personId, Guid projectId)
    {
        var schedule = (await _planner.GetFromJsonAsync<ScheduleResponse>("/api/schedule?from=2026-03-01&to=2026-03-31"))!;
        return schedule.People.Single(p => p.Id == personId).Projects.Single(p => p.ProjectId == projectId).Allocations.ToList();
    }

    [Fact]
    public async Task ProjectPeopleListsEachPersonWithAllocationsAndMineListsTheCallersProjects()
    {
        var project = await NewProject();
        var other = await NewProject();
        await Create(_floId, project.Id, March(9), March(10));
        await Create(_floId, project.Id, March(2), March(3));
        await Create(_annId, project.Id, March(4), March(5));

        var people = (await _viewer.GetFromJsonAsync<List<ProjectPersonResponse>>($"/api/projects/{project.Id}/people", cancellationToken: TestContext.Current.CancellationToken))!;
        var mine = (await _viewer.GetFromJsonAsync<List<Guid>>("/api/projects/mine", cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(["Ann Planner", "Flo Good"], people.Select(p => p.Name));
        Assert.Equal([March(2), March(9)], people[1].Allocations.Select(a => a.StartDate));
        Assert.Contains(project.Id, mine);
        Assert.DoesNotContain(other.Id, mine);
        Assert.Equal(HttpStatusCode.NotFound, (await _viewer.GetAsync($"/api/projects/{Guid.NewGuid()}/people", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ANonPlannerGets403OnEveryAllocationWrite()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(6));
        var bar = (await Bars(_floId, project.Id)).Single();

        var post = await _viewer.PostAsJsonAsync("/api/allocations", new CreateAllocationRequest(_floId, project.Id, March(2), March(6), 50), cancellationToken: TestContext.Current.CancellationToken);
        var put = await _viewer.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(March(2), March(6), 75), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task APlannerCreatesAnAllocationAndTheScheduleShowsItWithLoad()
    {
        var project = await NewProject("tentative");
        // An own Person, so Allocations from other tests in this class do not change the load.
        var person = new Person { Name = "Lo Ad", Email = $"{Unique("alloc-load")}@example.com" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.People.Add(person);
            db.Contracts.Add(new Contract { PersonId = person.Id, StartDate = March(1), HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await Create(person.Id, project.Id, March(2), March(6))).StatusCode);

        var schedule = (await _planner.GetFromJsonAsync<ScheduleResponse>("/api/schedule?from=2026-03-02&to=2026-03-08", cancellationToken: TestContext.Current.CancellationToken))!;
        var shown = schedule.People.Single(p => p.Id == person.Id);
        var row = shown.Projects.Single(p => p.ProjectId == project.Id);
        Assert.Equal("tentative", row.Status);
        Assert.Equal((March(2), March(6), 50), (row.Allocations[0].StartDate, row.Allocations[0].EndDate, row.Allocations[0].Percentage));
        Assert.Equal(0.5m, shown.Days[0].Utilisation);
    }

    [Fact]
    public async Task E5_TheLatestChangeWinsAndTheOldAllocationIsSplit()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(1), March(31), 50);

        await Create(_floId, project.Id, March(10), March(14), 100);

        var bars = (await Bars(_floId, project.Id)).OrderBy(b => b.StartDate).ToList();
        Assert.Equal([(March(1), March(9), 50), (March(10), March(14), 100), (March(15), March(31), 50)],
            bars.Select(b => (b.StartDate, b.EndDate, b.Percentage)));
    }

    [Fact]
    public async Task ChangingThePercentageKeepsTheDates()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(6));
        var bar = (await Bars(_floId, project.Id)).Single();

        var response = await _planner.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(bar.StartDate, bar.EndDate, 75), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var changed = (await Bars(_floId, project.Id)).Single();
        Assert.Equal((bar.Id, 75), (changed.Id, changed.Percentage));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(105)]
    public async Task AnInvalidPercentageGets400(int percentage)
    {
        var project = await NewProject();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(_floId, project.Id, March(2), March(6), percentage)).StatusCode);
    }

    [Fact]
    public async Task AnAllocationCannotCoverADayAfterTheEndDateOfThePerson()
    {
        var project = await NewProject();

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(_leaverId, project.Id, March(16), March(21))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Create(_leaverId, project.Id, March(16), March(20))).StatusCode);
    }

    [Fact]
    public async Task AnArchivedProjectRefusesAllocationWrites()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(6));
        var bar = (await Bars(_floId, project.Id)).Single();
        await _planner.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(project.Name, project.ClientId, "archived"), cancellationToken: TestContext.Current.CancellationToken);

        var post = await Create(_floId, project.Id, March(9), March(10));
        var put = await _planner.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(bar.StartDate, bar.EndDate, 75), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
    }

    [Fact]
    public async Task ProjectsShowTheirDatesAndCountAndDeletingOneDeletesItsAllocations()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(6));
        await Create(_annId, project.Id, March(9), March(13));

        var clients = (await _viewer.GetFromJsonAsync<List<ClientResponse>>("/api/clients", cancellationToken: TestContext.Current.CancellationToken))!;
        var listed = clients.SelectMany(c => c.Projects).Single(p => p.Id == project.Id);
        Assert.Equal((March(2), March(13), 2), (listed.StartDate, listed.EndDate, listed.AllocationCount));

        Assert.Equal(HttpStatusCode.NoContent, (await _planner.DeleteAsync($"/api/projects/{project.Id}", TestContext.Current.CancellationToken)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        Assert.False(await db.Allocations.AnyAsync(a => a.ProjectId == project.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EditingChangesPersonAndProjectOfTheSameAllocation()
    {
        var first = await NewProject();
        var second = await NewProject();
        await Create(_floId, first.Id, March(2), March(6));
        await Create(_annId, second.Id, March(4), March(10));
        var bar = (await Bars(_floId, first.Id)).Single();

        var response = await _planner.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(March(2), March(6), 75, _annId, second.Id), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // The moved Allocation wins on its days and keeps its id.
        var ann = (await Bars(_annId, second.Id)).OrderBy(b => b.StartDate).ToList();
        Assert.Contains(ann, b => b.Id == bar.Id && b.Percentage == 75 && b.StartDate == March(2) && b.EndDate == March(6));
        Assert.Contains(ann, b => b.StartDate == March(7) && b.EndDate == March(10) && b.Percentage == 50);
        var schedule = (await _planner.GetFromJsonAsync<ScheduleResponse>("/api/schedule?from=2026-03-01&to=2026-03-31", cancellationToken: TestContext.Current.CancellationToken))!;
        // Flo is not shown at all when no other test has given Flo an Allocation yet.
        Assert.DoesNotContain(schedule.People.Where(p => p.Id == _floId).SelectMany(p => p.Projects), p => p.ProjectId == first.Id);
    }

    [Fact]
    public async Task EditingToAnArchivedProjectOrAPastEndDateIsRefused()
    {
        var open = await NewProject();
        var archived = await NewProject();
        await _planner.PutAsJsonAsync($"/api/projects/{archived.Id}", new UpdateProjectRequest(archived.Name, archived.ClientId, "archived"), cancellationToken: TestContext.Current.CancellationToken);
        await Create(_floId, open.Id, March(2), March(6));
        var bar = (await Bars(_floId, open.Id)).Single();

        var toArchived = await _planner.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(March(2), March(6), 50, null, archived.Id), cancellationToken: TestContext.Current.CancellationToken);
        var toLeaver = await _planner.PutAsJsonAsync($"/api/allocations/{bar.Id}", new UpdateAllocationRequest(March(16), March(21), 50, _leaverId, null), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, toArchived.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toLeaver.StatusCode);
    }

    [Fact]
    public async Task SplitMakesTwoAllocationsAndTheSecondStartsOnTheSplitDate()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(13), 60);
        var bar = (await Bars(_floId, project.Id)).Single();

        var response = await _planner.PostAsJsonAsync($"/api/allocations/{bar.Id}/split", new SplitAllocationRequest(March(9)), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var bars = (await Bars(_floId, project.Id)).OrderBy(b => b.StartDate).ToList();
        Assert.Equal([(March(2), March(8), 60), (March(9), March(13), 60)], bars.Select(b => (b.StartDate, b.EndDate, b.Percentage)));
        Assert.Equal(bar.Id, bars[0].Id);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(14)]
    public async Task SplitOutsideTheAllocationOrOnItsFirstDayGets400(int day)
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(13));
        var bar = (await Bars(_floId, project.Id)).Single();

        var response = await _planner.PostAsJsonAsync($"/api/allocations/{bar.Id}/split", new SplitAllocationRequest(March(day)), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesTheAllocation()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(6));
        var bar = (await Bars(_floId, project.Id)).Single();

        Assert.Equal(HttpStatusCode.NoContent, (await _planner.DeleteAsync($"/api/allocations/{bar.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _planner.DeleteAsync($"/api/allocations/{bar.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ANonPlannerCannotSplitOrDeleteAndAnArchivedProjectRefusesBoth()
    {
        var project = await NewProject();
        await Create(_floId, project.Id, March(2), March(13));
        var bar = (await Bars(_floId, project.Id)).Single();
        var split = new SplitAllocationRequest(March(9));

        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.PostAsJsonAsync($"/api/allocations/{bar.Id}/split", split, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.DeleteAsync($"/api/allocations/{bar.Id}", TestContext.Current.CancellationToken)).StatusCode);

        await _planner.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(project.Name, project.ClientId, "archived"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, (await _planner.PostAsJsonAsync($"/api/allocations/{bar.Id}/split", split, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _planner.DeleteAsync($"/api/allocations/{bar.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Single(await Bars(_floId, project.Id));
    }
}
