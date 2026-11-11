using System.Net;
using System.Net.Http.Json;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Client.People;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Rehire a Person.
public class RehireTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 10, 30);
    private static readonly WeekHours Half = new(4, 4, 4, 4, 4, 0, 0);
    private readonly FakeEmailSender _mail;
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _ann = null!;
    private Guid _annId;

    public RehireTests(ClockedHeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
        _time = app.Time;
        _time.Set(ClockedHeadroomApp.Start);
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!db.People.Any())
        {
            var ann = new Person { Name = "Ann Planner", Email = "ann@example.com", IsPlanner = true };
            ann.LastChangedBy = ann.Id;
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.Add(ann);
            db.Contracts.Add(new Contract { PersonId = ann.Id, StartDate = Jan1, HoursMonday = 8 });
            await db.SaveChangesAsync();
        }
        await db.LoginCodes.ExecuteDeleteAsync();
        _annId = db.People.Single(p => p.Email == "ann@example.com").Id;
        _ann = await TestLogin.SignInAsync(_factory, _mail, "ann@example.com");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RehireWithAGapAddsAZeroHourContractThenTheNewContract()
    {
        var zed = await SeedLeaver();
        var returnDate = new DateOnly(2026, 12, 1);

        var response = await Send(HttpMethod.Post, $"/api/people/{zed}/rehire", JsonContent.Create(new RehireRequest(returnDate, Half)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Null(person.EndDate);
        Assert.Equal(_annId, person.LastChangedBy);
        Assert.Equal([Jan1, End.AddDays(1), returnDate], person.Contracts.Select(c => c.StartDate));
        Assert.Equal([40m, 0m, 20m], person.Contracts.Select(c => c.HoursPerWeek));
        Assert.All(person.Contracts.Skip(1), c => Assert.Equal(_annId, c.LastChangedBy));
    }

    [Fact]
    public async Task RehireTheDayAfterTheEndDateAddsNoZeroHourContract()
    {
        var zed = await SeedLeaver();

        var response = await Send(HttpMethod.Post, $"/api/people/{zed}/rehire", JsonContent.Create(new RehireRequest(End.AddDays(1), Half)));

        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal([Jan1, End.AddDays(1)], person.Contracts.Select(c => c.StartDate));
        Assert.Equal([40m, 20m], person.Contracts.Select(c => c.HoursPerWeek));
    }

    [Fact]
    public async Task RehireRefusesAReturnDateNotAfterTheEndDateAndAPersonWhoHasNotLeft()
    {
        var zed = await SeedLeaver();
        var tooEarly = await Send(HttpMethod.Post, $"/api/people/{zed}/rehire", JsonContent.Create(new RehireRequest(End, Half)));
        var active = await Run(db => db.People.Add(new Person { Name = "Active", Email = $"a.{Guid.NewGuid():N}@example.com" }).Entity.Id);
        var notLeft = await Send(HttpMethod.Post, $"/api/people/{active}/rehire", JsonContent.Create(new RehireRequest(new DateOnly(2026, 12, 1), Half)));

        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
        Assert.Contains(DisplayDate.Format(End), await tooEarly.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, notLeft.StatusCode);
    }

    [Fact]
    public void RehireDefaultsToTheLastContractWithHours()
    {
        var zero = new WeekHours(0, 0, 0, 0, 0, 0, 0);
        PersonResponse Person(params WeekHours[] hours) => new("person", Guid.NewGuid(), "Zed", null, false, End, PersonStatus.Left, false, 0, new PersonLocks(null, null, null, null),
            hours.Select(h => new ContractResponse("contract", Guid.NewGuid(), Guid.Empty, Jan1, null, h, h.Total, null, default)).ToList(), null, default);

        Assert.Equal(Half, EmploymentText.DefaultRehireHours(Person(WeekHours.FullTime, Half, zero)));
        Assert.Equal(WeekHours.FullTime, EmploymentText.DefaultRehireHours(Person(zero)));
    }

    private async Task<Guid> SeedLeaver()
    {
        var zed = await Run(db => db.People.Add(new Person { Name = "Zed Leaver", Email = $"zed.{Guid.NewGuid():N}@example.com", EndDate = End }).Entity.Id);
        await Run(db => db.Contracts.Add(new Contract
        {
            PersonId = zed, StartDate = Jan1,
            HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
        }));
        return zed;
    }

    private async Task<T> Run<T>(Func<HeadroomDbContext, T> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var result = work(db);
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent? body)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_ann));
        return await _ann.SendAsync(request);
    }
}
