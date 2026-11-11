using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Delete a Person by hand and automatically.
public class DeletePersonTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private readonly FakeEmailSender _mail;
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _ann = null!;
    private Guid _annId;

    public DeletePersonTests(ClockedHeadroomApp app)
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
    public async Task DeleteRemovesThePersonWithContractsAllocationsAndTimeOffEvenBeforeTheEndDate()
    {
        var zed = await Leaver(null, planner: true);
        await Run(db => db.Contracts.Add(new Contract { PersonId = zed, StartDate = new(2026, 6, 1), HoursMonday = 4 }));
        await AddAllocationAndTimeOff(zed);
        var other = await Leaver(null);
        await AddAllocationAndTimeOff(other);

        var response = await Delete(zed);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((0, 0, 0, 0), await Run(db => (db.People.Count(p => p.Id == zed), db.Contracts.Count(c => c.PersonId == zed),
            db.Allocations.Count(a => a.PersonId == zed), db.TimeOff.Count(t => t.PersonId == zed))));
        Assert.Equal((1, 1, 1), await Run(db => (db.Contracts.Count(c => c.PersonId == other), db.Allocations.Count(a => a.PersonId == other), db.TimeOff.Count(t => t.PersonId == other))));
    }

    [Fact]
    public async Task TheDeleteCheckCountsWhatTheDialogShows()
    {
        var zed = await Leaver(null);
        await Run(db => db.Contracts.Add(new Contract { PersonId = zed, StartDate = new(2026, 6, 1), HoursMonday = 4 }));
        await AddAllocationAndTimeOff(zed);

        var check = (await (await Send(HttpMethod.Get, $"/api/people/{zed}/delete-check", null)).Content
            .ReadFromJsonAsync<DeleteCheck>(cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(new DeleteCheck(2, 1, 1, null), check);
    }

    [Fact]
    public async Task DeleteRemovesThePasskeysAndAccessTokensOfThePerson()
    {
        var zed = await Leaver(null);
        await Run(db => db.Passkeys.Add(new Passkey { PersonId = zed, Name = "Laptop", CredentialId = Guid.NewGuid().ToByteArray(), PublicKey = [1], UserHandle = [1] }));
        await Run(db => db.AccessTokens.Add(new AccessToken { PersonId = zed, Name = "Script", TokenHash = SHA256.HashData(Guid.NewGuid().ToByteArray()) }));

        await Delete(zed);

        Assert.Equal((0, 0), await Run(db => (db.Passkeys.Count(p => p.PersonId == zed), db.AccessTokens.Count(t => t.PersonId == zed))));
    }

    [Fact]
    public async Task APersonWhoIsLastChangedByOnOtherRecordsCanBeDeleted()
    {
        var zed = await Leaver(null);
        var client = await Run(db => db.Clients.Add(new Headroom.Data.Client { Name = $"Acme {Guid.NewGuid():N}", LastChangedBy = zed }).Entity.Id);
        var other = await Leaver(null);
        await Run(db => db.People.Single(p => p.Id == other).LastChangedBy = zed);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(zed)).StatusCode);

        Assert.Equal(zed, await Run(db => db.Clients.Single(c => c.Id == client).LastChangedBy));
        Assert.Equal(zed, await Run(db => db.People.Single(p => p.Id == other).LastChangedBy));
    }

    [Fact]
    public async Task TheEmailOfADeletedPersonIsFreeForANewPerson()
    {
        var zed = await Leaver(new(2026, 11, 1));
        var email = await Run(db => db.People.Single(p => p.Id == zed).Email!);
        await Delete(zed);

        var added = await Send(HttpMethod.Post, "/api/people", JsonContent.Create(
            new AddPersonRequest("New Zed", email, false, Jan1, new WeekHours(8, 0, 0, 0, 0, 0, 0))));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
    }

    [Fact]
    public async Task TheLastPlannerWhoCanLogInCannotBeDeleted()
    {
        await Run(db => db.People.Where(p => p.IsPlanner && p.Id != _annId).ExecuteUpdate(s => s.SetProperty(p => p.IsPlanner, false)));

        var response = await Delete(_annId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("last Planner", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheDailyCheckDeletesOnTheFirstRequestOfADayForEndDates12MonthsAgoOrMore()
    {
        // The check runs once per day in each host, so this test starts its own.
        await using var host = _factory.WithWebHostBuilder(_ => { });
        var ann = await TestLogin.SignInAsync(host, _mail, "ann@example.com");
        // The next day is 3 November 2026, so the limit is 3 November 2025.
        var due = await Leaver(new(2025, 11, 3), planner: true);
        var older = await Leaver(new(2024, 6, 1));
        var notYet = await Leaver(new(2025, 11, 4));
        _time.Advance(TimeSpan.FromDays(1));

        await ann.GetAsync("/api/me", TestContext.Current.CancellationToken);

        var left = await Run(db => db.People.Where(p => new[] { due, older, notYet }.Contains(p.Id)).Select(p => p.Id).ToList());
        Assert.Equal([notYet], left);
        Assert.Equal(0, await Run(db => db.Contracts.Count(c => c.PersonId == due)));
    }

    [Fact]
    public async Task TheDailyCheckRunsAtStartup()
    {
        var due = await Leaver(new(2025, 11, 2));

        await using var host = _factory.WithWebHostBuilder(_ => { });
        _ = host.Services;

        Assert.Equal(0, await Run(db => db.People.Count(p => p.Id == due)));
    }

    [Fact]
    public async Task UtilisationNoLongerCountsADeletedPerson()
    {
        var zed = await Leaver(new(2026, 3, 25));
        const string url = "/api/utilisation?from=2026-03-23&to=2026-04-05&period=week";
        var before = (await _ann.GetFromJsonAsync<UtilisationResponse>(url, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Contains(before.People, p => p.PersonId == zed);

        await Delete(zed);

        var after = (await _ann.GetFromJsonAsync<UtilisationResponse>(url, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.DoesNotContain(after.People, p => p.PersonId == zed);
    }

    // One Allocation and one Time off for the Person.
    private async Task AddAllocationAndTimeOff(Guid person)
    {
        var client = await Run(db => db.Clients.Add(new Headroom.Data.Client { Name = $"Acme {Guid.NewGuid():N}" }).Entity);
        var project = await Run(db => db.Projects.Add(new Project { ClientId = client.Id, Name = "Live", Status = ProjectStatus.Confirmed }).Entity.Id);
        await Run(db => db.Allocations.Add(new Allocation { PersonId = person, ProjectId = project, StartDate = Jan1, EndDate = Jan1, Percentage = 50 }));
        await Run(db => db.TimeOff.Add(new Headroom.Data.TimeOff { PersonId = person, StartDate = Jan1, StartPart = DayPart.Morning, EndDate = Jan1, EndPart = DayPart.Afternoon }));
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

    private Task<HttpResponseMessage> Delete(Guid id) => Send(HttpMethod.Delete, $"/api/people/{id}", null);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent? body)
    {
        var request = new HttpRequestMessage(method, url) { Content = body };
        request.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(_ann));
        return await _ann.SendAsync(request);
    }
}
