using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Headroom.Tests.Api;

// Erase personal details by hand and automatically.
public class ErasePersonTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private readonly FakeEmailSender _mail = new();
    // Monday 2 November 2026 in Brussels.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _ann = null!;
    private Guid _annId;

    public ErasePersonTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEmailSender>(_mail);
            services.AddSingleton<TimeProvider>(_time);
        }));
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

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    [Fact]
    public async Task EraseRemovesTheDetailsAndKeepsTheRecordsUnderFormerPersonN()
    {
        var zed = await Leaver(new(2026, 11, 2), planner: true);
        var contract = await Run(db => db.Contracts.Single(c => c.PersonId == zed).Id);

        var response = await Erase(zed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((PersonStatus.Erased, $"Former Person {person.ErasedNumber}", null, false), (person.Status, person.Name, person.Email, person.IsPlanner));
        Assert.Equal(_annId, person.LastChangedBy);
        Assert.Equal(contract, Assert.Single(person.Contracts).Id);
        Assert.Equal("", await Run(db => db.People.Single(p => p.Id == zed).Name));
    }

    [Fact]
    public async Task TheEmailOfAnErasedPersonIsFreeForANewPerson()
    {
        var zed = await Leaver(new(2026, 11, 1));
        var email = await Run(db => db.People.Single(p => p.Id == zed).Email!);
        await Erase(zed);

        var added = await Send(HttpMethod.Post, "/api/people", JsonContent.Create(
            new AddPersonRequest("New Zed", email, false, Jan1, new WeekHours(8, 0, 0, 0, 0, 0, 0))));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
    }

    [Fact]
    public async Task ANumberIsNeverReusedAfterADelete()
    {
        var first = await Leaver(new(2026, 11, 1));
        var number = (await (await Erase(first)).Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!.ErasedNumber;
        await Run(db =>
        {
            db.Contracts.RemoveRange(db.Contracts.Where(c => c.PersonId == first));
            return db.People.Remove(db.People.Single(p => p.Id == first));
        });
        var second = await Leaver(new(2026, 11, 1));

        var next = (await (await Erase(second)).Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!.ErasedNumber;

        Assert.Equal(number + 1, next);
    }

    [Theory]
    [InlineData("2026-11-03")]
    [InlineData(null)]
    public async Task EraseIsRefusedWithoutAnEndDateTodayOrEarlier(string? end)
    {
        var zed = await Leaver(end is null ? null : DateOnly.Parse(end));

        Assert.Equal(HttpStatusCode.Conflict, (await Erase(zed)).StatusCode);
    }

    [Fact]
    public async Task TheLastPlannerWhoCanLogInCannotBeErased()
    {
        // Ann is the only Planner who can log in, and her end date is today: Erase is not allowed.
        await Run(db => db.People.Where(p => p.IsPlanner && p.Id != _annId).ExecuteUpdate(s => s.SetProperty(p => p.IsPlanner, false)));
        await Run(db => db.People.Where(p => p.Id == _annId).ExecuteUpdate(s => s.SetProperty(p => p.EndDate, new DateOnly(2026, 11, 2))));
        try
        {
            var response = await Erase(_annId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("last Planner", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await Run(db => db.People.Where(p => p.Id == _annId).ExecuteUpdate(s => s.SetProperty(p => p.EndDate, (DateOnly?)null)));
        }
    }

    [Fact]
    public async Task AnErasedPersonCannotBeErasedOrGetAnEndDateAgain()
    {
        var zed = await Leaver(new(2026, 11, 1));
        await Erase(zed);

        Assert.Equal(HttpStatusCode.Conflict, (await Erase(zed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2026, 12, 1))))).StatusCode);
    }

    [Fact]
    public async Task TheAutomaticEraseRunsOnTheFirstRequestOfADayForEndDates12MonthsAgoOrMore()
    {
        // The next day is 3 November 2026, so the limit is 3 November 2025.
        var due = await Leaver(new(2025, 11, 3), planner: true);
        var older = await Leaver(new(2024, 6, 1));
        var notYet = await Leaver(new(2025, 11, 4));
        _time.Advance(TimeSpan.FromDays(1));

        await _ann.GetAsync("/api/me", TestContext.Current.CancellationToken);

        var erased = await Run(db => db.People.Where(p => p.ErasedNumber != null).Select(p => p.Id).ToList());
        Assert.Contains(due, erased);
        Assert.Contains(older, erased);
        Assert.DoesNotContain(notYet, erased);
        var person = await Run(db => db.People.AsNoTracking().Single(p => p.Id == due));
        Assert.Equal(("", null, false, null), (person.Name, person.Email, person.IsPlanner, person.LastChangedBy));
        Assert.Equal(1, await Run(db => db.Contracts.Count(c => c.PersonId == due)));
    }

    [Fact]
    public async Task TheAutomaticEraseRunsAtStartup()
    {
        var due = await Leaver(new(2025, 11, 2));

        _ = _factory.WithWebHostBuilder(_ => { }).Services;

        Assert.NotNull(await Run(db => db.People.Single(p => p.Id == due).ErasedNumber));
    }

    [Fact]
    public async Task UtilisationShowsAnErasedPersonBeforeTheEndDateAndScheduleHidesThem()
    {
        var zed = await Leaver(new(2026, 3, 25));
        var number = (await (await Erase(zed)).Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!.ErasedNumber;

        var utilisation = (await _ann.GetFromJsonAsync<UtilisationResponse>("/api/utilisation?from=2026-03-23&to=2026-04-05&period=week", cancellationToken: TestContext.Current.CancellationToken))!;
        var schedule = (await _ann.GetFromJsonAsync<ScheduleResponse>("/api/schedule?from=2026-03-23&to=2026-03-29", cancellationToken: TestContext.Current.CancellationToken))!;

        var row = Assert.Single(utilisation.People, p => p.PersonId == zed);
        Assert.Equal($"Former Person {number}", row.Name);
        Assert.Equal(8m, row.Cells[0].Capacity);
        Assert.Equal(0m, row.Cells[1].Capacity);
        Assert.DoesNotContain(schedule.People, p => p.Id == zed);
    }

    // A Person with one Contract (8 hours on Mondays) from 1 January 2026 and the given end date.
    private async Task<Guid> Leaver(DateOnly? end, bool planner = false)
    {
        var id = await Run(db => db.People.Add(new Person
        {
            Name = "Zed Leaver", Email = $"zed.{Guid.NewGuid():N}@example.com", IsPlanner = planner, EndDate = end,
        }).Entity.Id);
        await Run(db => db.Contracts.Add(new Contract { PersonId = id, StartDate = Jan1, HoursMonday = 8 }));
        return id;
    }

    private async Task<T> Run<T>(Func<HeadroomDbContext, T> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var result = work(db);
        await db.SaveChangesAsync();
        return result;
    }

    private Task<HttpResponseMessage> Erase(Guid id) => Send(HttpMethod.Post, $"/api/people/{id}/erase", null);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent? body)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_ann));
        return await _ann.SendAsync(request);
    }
}
