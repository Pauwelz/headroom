using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class ProjectsApiTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _planner = null!;
    private HttpClient _viewer = null!;

    public ProjectsApiTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IEmailSender>(_mail)));
    }

    public async ValueTask InitializeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            if (!db.People.Any())
            {
                var ann = new Person { Name = "Ann Planner", Email = "ann@example.com", IsPlanner = true };
                ann.LastChangedBy = ann.Id;
                db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
                db.People.Add(ann);
                db.People.Add(new Person { Name = "Flo Good", Email = "flo@example.com", LastChangedBy = ann.Id });
                await db.SaveChangesAsync();
            }
        }
        _planner = await TestLogin.SignInAsync(_factory, _mail, "ann@example.com");
        _planner.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_planner));
        _viewer = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        _viewer.DefaultRequestHeaders.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_viewer));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    // Each test uses unique names, so tests can share the database.
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<ClientResponse> NewClient(string? name = null)
    {
        var response = await _planner.PostAsJsonAsync("/api/clients", new ClientRequest(name ?? Unique("Client")));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClientResponse>())!;
    }

    private async Task<ProjectResponse> NewProject(Guid clientId, string? name = null, string status = "tentative")
    {
        var response = await _planner.PostAsJsonAsync($"/api/clients/{clientId}/projects",
            new CreateProjectRequest(name ?? Unique("Project"), status));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private async Task<ClientResponse> Reload(Guid clientId) =>
        (await _viewer.GetFromJsonAsync<List<ClientResponse>>("/api/clients"))!.Single(c => c.Id == clientId);

    [Fact]
    public async Task ClientsListSortedByNameWithTheirProjects()
    {
        var suffix = Unique("sort");
        var b = await NewClient($"B-{suffix}");
        var a = await NewClient($"a-{suffix}");
        await NewProject(a.Id, "Zed");
        await NewProject(a.Id, "alpha");

        var list = (await _viewer.GetFromJsonAsync<List<ClientResponse>>("/api/clients", cancellationToken: TestContext.Current.CancellationToken))!;

        var mine = list.Where(c => c.Name.EndsWith(suffix)).ToList();
        Assert.Equal([a.Id, b.Id], mine.Select(c => c.Id));
        Assert.Equal(["alpha", "Zed"], mine[0].Projects.Select(p => p.Name));
        Assert.All(mine[0].Projects, p => { Assert.Null(p.StartDate); Assert.Equal(0, p.AllocationCount); });
    }

    [Fact]
    public async Task ClientNamesAreUniqueWithoutCase()
    {
        var name = Unique("Dup");
        await NewClient(name);

        var again = await _planner.PostAsJsonAsync("/api/clients", new ClientRequest(name.ToUpperInvariant()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task ProjectNamesAreUniqueWithinTheClientOnly()
    {
        var one = await NewClient();
        var two = await NewClient();
        await NewProject(one.Id, "Website");

        var same = await _planner.PostAsJsonAsync($"/api/clients/{one.Id}/projects", new CreateProjectRequest("WEBSITE", "tentative"), cancellationToken: TestContext.Current.CancellationToken);
        var other = await _planner.PostAsJsonAsync($"/api/clients/{two.Id}/projects", new CreateProjectRequest("Website", "confirmed"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public async Task ANewProjectIsTentativeOrConfirmed(string? status)
    {
        var client = await NewClient();

        var response = await _planner.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new CreateProjectRequest("P", status), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveThenUnarchiveGivesConfirmedAndArchivedCannotBecomeTentative()
    {
        var client = await NewClient();
        var project = await NewProject(client.Id);

        var archived = await Update(project, status: "archived");
        var backToTentative = await _planner.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest(project.Name, client.Id, "tentative"), cancellationToken: TestContext.Current.CancellationToken);
        var unarchived = await Update(project, status: "confirmed");

        Assert.Equal("archived", archived.Status);
        Assert.Equal(HttpStatusCode.BadRequest, backToTentative.StatusCode);
        Assert.Equal("confirmed", unarchived.Status);
    }

    [Fact]
    public async Task AProjectCanBeRenamedAndMovedButNotToATakenName()
    {
        var from = await NewClient();
        var to = await NewClient();
        var project = await NewProject(from.Id, "Old");
        await NewProject(to.Id, "Taken");

        var moved = await Update(project, name: "New", clientId: to.Id);
        var clash = await _planner.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("taken", to.Id, "tentative"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(to.Id, moved.ClientId);
        Assert.Equal("New", moved.Name);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task AClientCanBeRenamedAndDeletedOnlyWithoutProjects()
    {
        var client = await NewClient();
        var project = await NewProject(client.Id);
        var newName = Unique("Renamed");

        var renamed = await _planner.PutAsJsonAsync($"/api/clients/{client.Id}", new ClientRequest(newName), cancellationToken: TestContext.Current.CancellationToken);
        var blocked = await _planner.DeleteAsync($"/api/clients/{client.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, (await _planner.DeleteAsync($"/api/projects/{project.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var deleted = await _planner.DeleteAsync($"/api/clients/{client.Id}", TestContext.Current.CancellationToken);

        Assert.Equal(newName, (await renamed.Content.ReadFromJsonAsync<ClientResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Name);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task AClientWithOnlyArchivedProjectsIsLeftOutOfPickers()
    {
        var client = await NewClient();
        Assert.True((await Reload(client.Id)).InPickers); // no Projects yet
        var project = await NewProject(client.Id);
        Assert.True((await Reload(client.Id)).InPickers);

        await Update(project, status: "archived");

        Assert.False((await Reload(client.Id)).InPickers);
    }

    [Fact]
    public async Task ANonPlannerCanReadButNotWrite()
    {
        var client = await NewClient();
        var project = await NewProject(client.Id);

        Assert.Equal(HttpStatusCode.OK, (await _viewer.GetAsync("/api/clients", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.PostAsJsonAsync("/api/clients", new ClientRequest("X"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.PutAsJsonAsync($"/api/clients/{client.Id}", new ClientRequest("X"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.DeleteAsync($"/api/clients/{client.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.PostAsJsonAsync($"/api/clients/{client.Id}/projects", new CreateProjectRequest("X", "tentative"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.PutAsJsonAsync($"/api/projects/{project.Id}", new UpdateProjectRequest("X", client.Id, "tentative"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.DeleteAsync($"/api/projects/{project.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheProjectViewShowsAnErasedPersonAsFormerPersonN()
    {
        var client = await NewClient();
        var project = await NewProject(client.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            var erased = new Person { Name = "", ErasedNumber = 42, EndDate = new DateOnly(2025, 1, 1) };
            db.People.Add(erased);
            db.Allocations.Add(new Allocation
            {
                PersonId = erased.Id, ProjectId = project.Id, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 2, 1), Percentage = 50,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var people = (await _viewer.GetFromJsonAsync<List<ProjectPersonResponse>>($"/api/projects/{project.Id}/people", cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal("Former Person 42", Assert.Single(people).Name);
    }

    private async Task<ProjectResponse> Update(ProjectResponse project, string? name = null, Guid? clientId = null, string? status = null)
    {
        var response = await _planner.PutAsJsonAsync($"/api/projects/{project.Id}",
            new UpdateProjectRequest(name ?? project.Name, clientId ?? project.ClientId, status ?? project.Status));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }
}
