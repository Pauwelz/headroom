using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class TimeOffTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    // Monday 2 March 2026.
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private Guid _zed;
    private Guid _gone;

    public TimeOffTests(HeadroomApp app)
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
            var ann = new Person { Name = "Ann", Email = "off-ann@example.com", IsPlanner = true };
            var zed = new Person { Name = "Zed", Email = "off-zed@example.com" };
            var gone = new Person { Name = "Gone", Email = "off-gone@example.com", EndDate = Mon.AddDays(2) };
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.AddRange(ann, zed, gone);
            foreach (var p in new[] { ann, zed, gone })
                db.Contracts.Add(new Contract
                {
                    PersonId = p.Id, StartDate = new(2026, 1, 1),
                    HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
                });
            await db.SaveChangesAsync();
        }
        _zed = await db.People.Where(p => p.Name == "Zed").Select(p => p.Id).SingleAsync();
        _gone = await db.People.Where(p => p.Name == "Gone").Select(p => p.Id).SingleAsync();
        db.TimeOff.RemoveRange(db.TimeOff);
        await db.SaveChangesAsync();
        await db.LoginCodes.ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task OverlappingTimeOffMergesAndCapacityFollows()
    {
        var (ann, token) = await SignIn("off-ann@example.com");
        // E7: Mon morning to Wed afternoon, then Wed afternoon to Fri afternoon.
        Assert.Equal(HttpStatusCode.OK, (await Add(ann, token, _zed, Mon, "morning", Mon.AddDays(2), "afternoon")).StatusCode);

        var merged = await Add(ann, token, _zed, Mon.AddDays(2), "afternoon", Mon.AddDays(4), "afternoon");

        var bar = (await merged.Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((Mon, "morning", Mon.AddDays(4), "afternoon"), (bar.StartDate, bar.StartPart, bar.EndDate, bar.EndPart));
        var schedule = await Schedule(ann);
        var zed = schedule.People.Single(p => p.Id == _zed);
        Assert.Equal([bar.Id], zed.TimeOff.Select(t => t.Id));
        Assert.All(zed.Days.Take(5), d => Assert.Equal(0m, d.Capacity));
    }

    [Fact]
    public async Task AHalfDayChangesCapacityLikeE6AndAnEditKeepsTheId()
    {
        var (ann, token) = await SignIn("off-ann@example.com");
        var created = await (await Add(ann, token, _zed, Mon.AddDays(2), "afternoon", Mon.AddDays(4), "morning")).Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([8m, 8m, 4m, 0m, 4m], (await Schedule(ann)).People.Single(p => p.Id == _zed).Days.Take(5).Select(d => d.Capacity));

        var edit = await Send(ann, token, HttpMethod.Put, $"/api/time-off/{created!.Id}",
            new TimeOffRequest(Mon.AddDays(2), "morning", Mon.AddDays(4), "morning"));

        Assert.Equal(created.Id, (await edit.Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken))!.Id);
        Assert.Equal([8m, 8m, 0m, 0m, 4m], (await Schedule(ann)).People.Single(p => p.Id == _zed).Days.Take(5).Select(d => d.Capacity));
    }

    [Fact]
    public async Task DeleteRemovesTheTimeOff()
    {
        var (ann, token) = await SignIn("off-ann@example.com");
        var created = await (await Add(ann, token, _zed, Mon, "morning", Mon, "afternoon")).Content.ReadFromJsonAsync<ScheduleTimeOff>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(ann, token, HttpMethod.Delete, $"/api/time-off/{created!.Id}", null)).StatusCode);

        Assert.Empty((await Schedule(ann)).People.Single(p => p.Id == _zed).TimeOff);
    }

    [Fact]
    public async Task TimeOffAfterTheEndDateIsRefused()
    {
        var (ann, token) = await SignIn("off-ann@example.com");

        Assert.Equal(HttpStatusCode.OK, (await Add(ann, token, _gone, Mon, "morning", Mon.AddDays(2), "afternoon")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(ann, token, _gone, Mon.AddDays(2), "morning", Mon.AddDays(3), "morning")).StatusCode);
    }

    [Fact]
    public async Task AnEndBeforeTheStartIsRefused()
    {
        var (ann, token) = await SignIn("off-ann@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await Add(ann, token, _zed, Mon, "afternoon", Mon, "morning")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(ann, token, _zed, Mon, "noon", Mon, "afternoon")).StatusCode);
    }

    [Fact]
    public async Task NonPlannerGets403OnEveryTimeOffWrite()
    {
        var (zed, token) = await SignIn("off-zed@example.com");
        var id = Guid.NewGuid();
        var body = new TimeOffRequest(Mon, "morning", Mon, "afternoon");

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(zed, token, HttpMethod.Post, $"/api/people/{_zed}/time-off", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(zed, token, HttpMethod.Put, $"/api/time-off/{id}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(zed, token, HttpMethod.Delete, $"/api/time-off/{id}", null)).StatusCode);
    }

    private async Task<(HttpClient Client, string Token)> SignIn(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, string token, Guid person, DateOnly start, string startPart, DateOnly end, string endPart) =>
        Send(client, token, HttpMethod.Post, $"/api/people/{person}/time-off", new TimeOffRequest(start, startPart, end, endPart));

    private static async Task<ScheduleResponse> Schedule(HttpClient client) =>
        (await client.GetFromJsonAsync<ScheduleResponse>("/api/schedule?from=2026-03-02&to=2026-03-08"))!;

    private static Task<HttpResponseMessage> Send(HttpClient client, string token, HttpMethod method, string url, TimeOffRequest? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(AntiforgeryToken.HeaderName, token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
