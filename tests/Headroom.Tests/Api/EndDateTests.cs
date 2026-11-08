using System.Net;
using System.Net.Http.Json;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// End date: the cut, the refusals, remove, and the login stop.
public class EndDateTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 11, 13);
    private readonly FakeEmailSender _mail;
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _ann = null!;
    private Guid _annId;

    public EndDateTests(ClockedHeadroomApp app)
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
        // Each test starts with Ann as the only Planner who can log in.
        await db.People.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, p => p.Email == "ann@example.com"));
        await db.People.Where(p => p.Email == "ann@example.com").ExecuteUpdateAsync(s => s.SetProperty(p => p.EndDate, (DateOnly?)null));
        _annId = db.People.Single(p => p.Email == "ann@example.com").Id;
        _ann = await TestLogin.SignInAsync(_factory, _mail, "ann@example.com");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TheCutDeletesOrShortensRecordsAfterTheEndDateAlsoOnArchivedProjects()
    {
        var (zed, archived, active) = await Seed();
        var kept = await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = active, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), Percentage = 50 }).Entity.Id);
        var shortened = await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = archived, StartDate = new(2026, 11, 9), EndDate = new(2026, 11, 27), Percentage = 50 }).Entity.Id);
        var deleted = await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = active, StartDate = new(2026, 11, 16), EndDate = new(2026, 11, 20), Percentage = 50 }).Entity.Id);
        var toShorten = await Run(db => db.TimeOff.Add(new Headroom.Data.TimeOff { PersonId = zed, StartDate = new(2026, 11, 12), StartPart = DayPart.Morning, EndDate = new(2026, 11, 18), EndPart = DayPart.Morning }).Entity.Id);
        var toDelete = await Run(db => db.TimeOff.Add(new Headroom.Data.TimeOff { PersonId = zed, StartDate = new(2026, 11, 16), StartPart = DayPart.Morning, EndDate = new(2026, 11, 17), EndPart = DayPart.Afternoon }).Entity.Id);
        var laterContract = await Run(db => db.Contracts.Add(new Contract { PersonId = zed, StartDate = new(2026, 12, 1), HoursMonday = 8 }).Entity.Id);

        var response = await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(End)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var person = (await response.Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((End, PersonStatus.Leaving), (person.EndDate, person.Status));
        Assert.Equal(_annId, person.LastChangedBy);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        Assert.Equal(new DateOnly(2026, 10, 31), (await db.Allocations.FindAsync(new object?[] { kept }, TestContext.Current.CancellationToken))!.EndDate);
        var cutAllocation = (await db.Allocations.FindAsync(new object?[] { shortened }, TestContext.Current.CancellationToken))!;
        Assert.Equal((End, _annId), (cutAllocation.EndDate, cutAllocation.LastChangedBy));
        Assert.Null(await db.Allocations.FindAsync(new object?[] { deleted }, TestContext.Current.CancellationToken));
        var cutTimeOff = (await db.TimeOff.FindAsync(new object?[] { toShorten }, TestContext.Current.CancellationToken))!;
        Assert.Equal((End, DayPart.Afternoon, _annId), (cutTimeOff.EndDate, cutTimeOff.EndPart, cutTimeOff.LastChangedBy));
        Assert.Null(await db.TimeOff.FindAsync(new object?[] { toDelete }, TestContext.Current.CancellationToken));
        Assert.Null(await db.Contracts.FindAsync(new object?[] { laterContract }, TestContext.Current.CancellationToken));
        Assert.Single(person.Contracts);
    }

    [Fact]
    public async Task ARecordCutByAnEarlierEndDateDoesNotComeBack()
    {
        var (zed, _, active) = await Seed();
        var allocation = await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = active, StartDate = new(2026, 11, 9), EndDate = new(2026, 11, 27), Percentage = 50 }).Entity.Id);
        await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(End)));

        await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2026, 12, 31))));

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(End, (await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Allocations.FindAsync(new object?[] { allocation }, TestContext.Current.CancellationToken))!.EndDate);
    }

    [Fact]
    public async Task TheImpactNamesTheCutRecordsAndChangesNothing()
    {
        var (zed, archived, active) = await Seed();
        var clientName = await Run(db => db.Clients.Single(c => db.Projects.Any(p => p.Id == active && p.ClientId == c.Id)).Name);
        await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = archived, StartDate = new(2026, 11, 9), EndDate = new(2026, 11, 27), Percentage = 50 }));
        await Run(db => db.Allocations.Add(new Allocation { PersonId = zed, ProjectId = active, StartDate = new(2026, 11, 16), EndDate = new(2026, 11, 20), Percentage = 25 }));

        var impact = await _ann.GetFromJsonAsync<EndDateImpact>($"/api/people/{zed}/end-date-impact?endDate=2026-11-13", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((End, new DateOnly(2026, 11, 14)), (impact!.LastWorkingDay, impact.LoginStopsOn));
        Assert.Collection(impact.Records,
            r => Assert.Equal(new CutRecord("Allocation", $"{clientName} / Closed, 09/11/2026 to 27/11/2026, 50%", false), r),
            r => Assert.Equal(new CutRecord("Allocation", $"{clientName} / Live, 16/11/2026 to 20/11/2026, 25%", true), r));
        using var scope = _factory.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.FindAsync(new object?[] { zed }, TestContext.Current.CancellationToken))!.EndDate);
    }

    [Fact]
    public async Task AnEndDateBeforeTheFirstContractIsRefused()
    {
        var (zed, _, _) = await Seed();

        var response = await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2025, 12, 31))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheLastPlannerWhoCanLogInCannotGetAnEndDate()
    {
        var response = await Send(HttpMethod.Put, $"/api/people/{_annId}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2027, 1, 1))));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("last Planner", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoPlannersWithAFutureEndDatePass()
    {
        var (bea, beaEmail) = await AddPlanner();
        var future = JsonContent.Create(new SetEndDateRequest(new DateOnly(2027, 1, 1)));

        var annFirst = await Send(HttpMethod.Put, $"/api/people/{_annId}/end-date", future);
        var beaSecond = await Send(HttpMethod.Put, $"/api/people/{bea}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2027, 2, 1))));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (annFirst.StatusCode, beaSecond.StatusCode));
    }

    [Fact]
    public async Task RemoveWorksWhileTheEndDateIsTodayOrLater()
    {
        var (zed, _, _) = await Seed();
        await Send(HttpMethod.Put, $"/api/people/{zed}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2026, 11, 2))));

        var response = await Send(HttpMethod.Delete, $"/api/people/{zed}/end-date", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<PersonResponse>(cancellationToken: TestContext.Current.CancellationToken))!.EndDate);
    }

    [Fact]
    public async Task RemoveIsRefusedAfterTheEndDateAndNeedsRehire()
    {
        var (zed, _, _) = await Seed();
        await Run(db => db.People.Single(p => p.Id == zed).EndDate = new DateOnly(2026, 11, 1));

        var response = await Send(HttpMethod.Delete, $"/api/people/{zed}/end-date", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Rehire", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenSessionsEndTheDayAfterTheEndDate()
    {
        var (bea, beaEmail) = await AddPlanner();
        var beaClient = await TestLogin.SignInAsync(_factory, _mail, beaEmail);
        await Send(HttpMethod.Put, $"/api/people/{bea}/end-date", JsonContent.Create(new SetEndDateRequest(new DateOnly(2026, 11, 2))));
        Assert.Equal(HttpStatusCode.OK, (await beaClient.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);

        _time.Advance(TimeSpan.FromDays(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await beaClient.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    // Zed has one Contract from 1 January 2026 and no end date. Returns Zed, an Archived Project and a Confirmed Project.
    private async Task<(Guid Zed, Guid Archived, Guid Active)> Seed()
    {
        var zed = await Run(db => db.People.Add(new Person { Name = "Zed Leaver", Email = $"zed.{Guid.NewGuid():N}@example.com" }).Entity.Id);
        await Run(db => db.Contracts.Add(new Contract { PersonId = zed, StartDate = Jan1, HoursMonday = 8 }));
        var client = await Run(db => db.Clients.Add(new Headroom.Data.Client { Name = $"Acme Corp {Guid.NewGuid():N}" }).Entity);
        var archived = await Run(db => db.Projects.Add(new Project { ClientId = client.Id, Name = "Closed", Status = ProjectStatus.Archived }).Entity.Id);
        var active = await Run(db => db.Projects.Add(new Project { ClientId = client.Id, Name = "Live", Status = ProjectStatus.Confirmed }).Entity.Id);
        return (zed, archived, active);
    }

    private async Task<(Guid Id, string Email)> AddPlanner()
    {
        var email = $"bea.{Guid.NewGuid():N}@example.com";
        var id = await Run(db => db.People.Add(new Person { Name = "Bea Planner", Email = email, IsPlanner = true }).Entity.Id);
        await Run(db => db.Contracts.Add(new Contract { PersonId = id, StartDate = Jan1, HoursMonday = 8 }));
        return (id, email);
    }

    private async Task<T> Run<T>(Func<HeadroomDbContext, T> add)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var result = add(db);
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
