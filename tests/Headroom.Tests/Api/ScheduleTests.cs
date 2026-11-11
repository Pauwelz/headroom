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

public class ScheduleTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/schedule?from=2026-03-02&to=2026-03-08";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;

    public ScheduleTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!db.People.Any())
        {
            var start = new DateOnly(2026, 1, 1);
            var ann = new Person { Name = "Ann Planner", Email = "sched-ann@example.com", IsPlanner = true };
            var zed = new Person { Name = "Zed Worker", Email = "sched-zed@example.com" };
            var bob = new Person { Name = "Bob Worker", Email = "sched-bob@example.com" };
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.AddRange(ann, zed, bob);
            foreach (var p in new[] { ann, zed, bob })
                db.Contracts.Add(new Contract
                {
                    PersonId = p.Id, StartDate = start,
                    HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
                });
            db.CompanyDaysOff.Add(new CompanyDayOff { Date = new(2026, 3, 4), Name = "Closure" });
            await db.SaveChangesAsync();
        }
        await db.LoginCodes.ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ARequestWithoutLoginGets401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task AnyLoggedInPersonSeesPeopleSortedByName()
    {
        var zed = await TestLogin.SignInAsync(_factory, _mail, "sched-zed@example.com");

        var schedule = (await zed.GetFromJsonAsync<ScheduleResponse>(Url, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(["Ann Planner", "Bob Worker", "Zed Worker"], schedule.People.Select(p => p.Name));
        Assert.Equal(7, schedule.People[0].Days.Count);
    }

    [Fact]
    public async Task ARangeOf140DaysIsAcceptedAnd141IsRejected()
    {
        var zed = await TestLogin.SignInAsync(_factory, _mail, "sched-zed@example.com");

        var ok = await zed.GetAsync("/api/schedule?from=2026-03-02&to=2026-07-19", TestContext.Current.CancellationToken);
        var tooLong = await zed.GetAsync("/api/schedule?from=2026-03-02&to=2026-07-20", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task CapacityComesFromTheRulesWithCompanyDaysOff()
    {
        var ann = await TestLogin.SignInAsync(_factory, _mail, "sched-ann@example.com");

        var schedule = (await ann.GetFromJsonAsync<ScheduleResponse>(Url, cancellationToken: TestContext.Current.CancellationToken))!;

        var days = schedule.People[0].Days;
        Assert.Equal([8m, 8m, 0m, 8m, 8m, 0m, 0m], days.Select(d => d.Capacity));
        Assert.Contains(schedule.DaysOff, d => d is { Type: DayOffTypeName.CompanyDayOff, Name: "Closure" } && d.Date == new DateOnly(2026, 3, 4));
    }

    [Fact]
    public async Task EveryObjectHasATypeAndRangesUseStartDateAndEndDate()
    {
        var ann = await TestLogin.SignInAsync(_factory, _mail, "sched-ann@example.com");

        using var schedule = JsonDocument.Parse(await ann.GetStringAsync(Url, TestContext.Current.CancellationToken));
        using var utilisation = JsonDocument.Parse(await ann.GetStringAsync("/api/utilisation?from=2026-03-02&to=2026-03-08&period=week", TestContext.Current.CancellationToken));

        var root = schedule.RootElement;
        Assert.Equal(("Schedule", "2026-03-02", "2026-03-08"),
            (root.GetProperty("type").GetString(), root.GetProperty("startDate").GetString(), root.GetProperty("endDate").GetString()));
        Assert.Equal("CompanyDayOff", root.GetProperty("daysOff")[0].GetProperty("type").GetString());
        var day = root.GetProperty("people")[0].GetProperty("days")[0];
        Assert.Equal("ScheduleDay", day.GetProperty("type").GetString());
        Assert.True(day.TryGetProperty("utilisation", out _));
        Assert.False(day.TryGetProperty("load", out _));

        var report = utilisation.RootElement;
        Assert.Equal(("Utilisation", "2026-03-02"), (report.GetProperty("type").GetString(), report.GetProperty("startDate").GetString()));
        Assert.Equal("UtilisationPeriod", report.GetProperty("periods")[0].GetProperty("type").GetString());
        Assert.Equal("2026-03-08", report.GetProperty("periods")[0].GetProperty("endDate").GetString());
        var row = report.GetProperty("all");
        Assert.Equal("UtilisationRow", row.GetProperty("type").GetString());
        Assert.Equal("UtilisationCell", row.GetProperty("total").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("from=2026-03-08&to=2026-03-02")]
    [InlineData("from=2026-01-01&to=2026-12-31")]
    public async Task AnInvalidRangeGets400(string query)
    {
        var ann = await TestLogin.SignInAsync(_factory, _mail, "sched-ann@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await ann.GetAsync($"/api/schedule?{query}", TestContext.Current.CancellationToken)).StatusCode);
    }
}
