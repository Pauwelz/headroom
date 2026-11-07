using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class PeopleTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _planner = null!;

    public PeopleTests(HeadroomApp app)
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
                db.Contracts.Add(new Contract { PersonId = ann.Id, StartDate = Jan1, HoursMonday = 8 });
                await db.SaveChangesAsync();
            }
            // Every test logs in again. The login rate limit would stop the later tests.
            await db.LoginCodes.ExecuteDeleteAsync();
        }
        _planner = await TestLogin.SignInAsync(_factory, _mail, "ann@example.com");
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    [Fact]
    public async Task ANonPlannerGets403OnEveryEndpoint()
    {
        var flo = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        var id = Guid.NewGuid();
        JsonContent Body() => Json(new ContractRequest(Jan1, WeekHours.FullTime));

        Assert.Equal(HttpStatusCode.Forbidden, (await flo.GetAsync("/api/people", TestContext.Current.CancellationToken)).StatusCode);
        foreach (var response in new[]
        {
            await Send(flo, HttpMethod.Post, "/api/people", Body()),
            await Send(flo, HttpMethod.Put, $"/api/people/{id}", Body()),
            await Send(flo, HttpMethod.Post, $"/api/people/{id}/contracts", Body()),
            await Send(flo, HttpMethod.Put, $"/api/people/{id}/contracts/{id}", Body()),
            await Send(flo, HttpMethod.Delete, $"/api/people/{id}/contracts/{id}", null),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task AddingAPersonCreatesTheFirstContractAndShowsInTheList()
    {
        var created = await Add("New One", "new.one@example.com");

        var person = (await List()).Single(p => p.Id == created.Id);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.Equal(40, person.HoursPerWeekNow);
        var contract = Assert.Single(person.Contracts);
        Assert.Null(contract.EndDate);
        Assert.Equal(Jan1, contract.StartDate);
    }

    [Fact]
    public async Task AnEmailIsUniqueWithoutCase()
    {
        await Add("Dup One", "dup@example.com");

        var response = await Send(_planner, HttpMethod.Post, "/api/people", Json(new AddPersonRequest(
            "Dup Two", "DUP@Example.com", false, Jan1, WeekHours.FullTime)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(-1)]
    [InlineData(8.1)]
    public async Task HoursOutsideTheRulesAreRefused(double monday)
    {
        var hours = WeekHours.FullTime with { Monday = (decimal)monday };

        var response = await Send(_planner, HttpMethod.Post, "/api/people", Json(new AddPersonRequest(
            "Bad Hours", "bad.hours@example.com", false, Jan1, hours)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditChangesNameAndEmail()
    {
        var created = await Add("Edit Me", "edit.me@example.com");

        var response = await Send(_planner, HttpMethod.Put, $"/api/people/{created.Id}", Json(new EditPersonRequest(
            "Edited", "edited@example.com", false)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var person = (await List()).Single(p => p.Id == created.Id);
        Assert.Equal(("Edited", "edited@example.com"), (person.Name, person.Email));
    }

    [Fact]
    public async Task TheListCarriesTheLockReasonsFromTheServer()
    {
        var flo = (await List()).Single(p => p.Email == "flo@example.com");

        Assert.Null(flo.Locks.SetEndDate);
        Assert.NotNull(flo.Locks.RemoveEndDate);
        Assert.NotNull(flo.Locks.Rehire);
        Assert.NotNull(flo.Locks.Erase);
    }

    [Fact]
    public async Task TheLastLoginPlannerKeepsTheFlag()
    {
        // Ann is the only Planner at this point of the class. Another test adds none.
        var ann = (await List()).Single(p => p.Email == "ann@example.com");

        var response = await Send(_planner, HttpMethod.Put, $"/api/people/{ann.Id}", Json(new EditPersonRequest(
            ann.Name, ann.Email!, false)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("last Planner", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnotherLoginPlannerLetsAPlannerLoseTheFlag()
    {
        var other = await Add("Second Planner", "second.planner@example.com", isPlanner: true);
        var ann = (await List()).Single(p => p.Email == "ann@example.com");

        var response = await Send(_planner, HttpMethod.Put, $"/api/people/{other.Id}", Json(new EditPersonRequest(
            other.Name, other.Email!, false)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(ann.IsPlanner);
    }

    [Fact]
    public async Task ContractsCanBeAddedEditedAndDeletedWithDerivedEndDates()
    {
        var created = await Add("Contract Person", "contract.person@example.com");
        var url = $"/api/people/{created.Id}/contracts";
        var second = new ContractRequest(new DateOnly(2026, 7, 1), WeekHours.Presets[2].Hours);

        var add = await Send(_planner, HttpMethod.Post, url, Json(second));
        var added = (await add.Content.ReadFromJsonAsync<ContractResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        var rows = (await List()).Single(p => p.Id == created.Id).Contracts;
        Assert.Equal(new DateOnly(2026, 6, 30), rows[0].EndDate);
        Assert.Null(rows[1].EndDate);

        var edit = await Send(_planner, HttpMethod.Put, $"{url}/{added.Id}", Json(second with { Hours = WeekHours.FullTime }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(_planner, HttpMethod.Delete, $"{url}/{added.Id}", null)).StatusCode);
        Assert.Single((await List()).Single(p => p.Id == created.Id).Contracts);
    }

    [Fact]
    public async Task TwoContractsCannotStartOnTheSameDate()
    {
        var created = await Add("Same Date", "same.date@example.com");

        var response = await Send(_planner, HttpMethod.Post, $"/api/people/{created.Id}/contracts",
            Json(new ContractRequest(Jan1, WeekHours.FullTime)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task APersonKeepsAtLeastOneContract()
    {
        var created = await Add("Only Contract", "only.contract@example.com");

        var response = await Send(_planner, HttpMethod.Delete,
            $"/api/people/{created.Id}/contracts/{created.Contracts.Single().Id}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AnEndedPersonCannotGetAContractAfterTheEndDate()
    {
        var created = await Add("Leaver", "leaver@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            (await db.People.FirstAsync(p => p.Id == created.Id, cancellationToken: TestContext.Current.CancellationToken)).EndDate = new DateOnly(2026, 3, 31);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await Send(_planner, HttpMethod.Post, $"/api/people/{created.Id}/contracts",
            Json(new ContractRequest(new DateOnly(2026, 4, 1), WeekHours.FullTime)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PersonStatus.Left, (await List()).Single(p => p.Id == created.Id).Status);
    }

    [Theory]
    [InlineData(null, null, "2026-01-01", "2026-06-01", PersonStatus.Active)]
    [InlineData(null, null, "2026-06-02", "2026-06-01", PersonStatus.StartsLater)]
    [InlineData(null, "2026-06-01", "2026-01-01", "2026-06-01", PersonStatus.Leaving)]
    [InlineData(null, "2026-05-31", "2026-01-01", "2026-06-01", PersonStatus.Left)]
    [InlineData(3, "2026-05-31", "2026-01-01", "2026-06-01", PersonStatus.Erased)]
    public void StatusFollowsTheLifecycleRules(int? erased, string? end, string start, string today, string expected) =>
        Assert.Equal(expected, PersonStatus.Of(
            erased, end is null ? null : DateOnly.Parse(end), DateOnly.Parse(start), DateOnly.Parse(today)));

    private async Task<PersonResponse> Add(string name, string email, bool isPlanner = false)
    {
        var response = await Send(_planner, HttpMethod.Post, "/api/people", Json(new AddPersonRequest(
            name, email, isPlanner, Jan1, WeekHours.FullTime)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonResponse>())!;
    }

    private async Task<List<PersonResponse>> List() =>
        (await _planner.GetFromJsonAsync<List<PersonResponse>>("/api/people"))!;

    private static JsonContent Json<T>(T value) => JsonContent.Create(value);

    private async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, HttpContent? body)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(client));
        return await client.SendAsync(request);
    }
}
