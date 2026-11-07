using System.Net;
using System.Net.Http.Json;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Client.Settings;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class SettingsTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/company-days-off";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;

    public SettingsTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann", Email = "settings-ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Flo", Email = "settings-flo@example.com", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task NonPlannerGets403OnEverySettingsEndpoint()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "settings-flo@example.com");
        var token = await TestLogin.ApiTokenAsync(client);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, token, HttpMethod.Post, Url, new DayOffRequest(new(2026, 5, 15), "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, token, HttpMethod.Put, $"{Url}/{id}", new DayOffRequest(new(2026, 5, 15), "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, token, HttpMethod.Delete, $"{Url}/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}?year=2026", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PlannerAddsEditsAndDeletesACompanyDayOff()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "settings-ann@example.com");
        var token = await TestLogin.ApiTokenAsync(client);

        var added = await Send(client, token, HttpMethod.Post, Url, new DayOffRequest(new(2031, 5, 2), " Bridge day "));
        var day = (await added.Content.ReadFromJsonAsync<DayOffResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal("Bridge day", day.Name);

        var edited = await Send(client, token, HttpMethod.Put, $"{Url}/{day.Id}", new DayOffRequest(new(2031, 5, 3), "Closure"));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var list = await client.GetFromJsonAsync<List<DayOffResponse>>($"{Url}?year=2031", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(list!, d => d.Type == "CompanyDayOff" && d.Date == new DateOnly(2031, 5, 3) && d.Name == "Closure");
        Assert.DoesNotContain(list!, d => d.Date == new DateOnly(2031, 5, 2) && d.Type == "CompanyDayOff");

        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, token, HttpMethod.Delete, $"{Url}/{day.Id}", null)).StatusCode);
        list = await client.GetFromJsonAsync<List<DayOffResponse>>($"{Url}?year=2031", cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(list!, d => d.Type == "CompanyDayOff");
    }

    [Fact]
    public async Task ListShowsBelgianPublicHolidaysWithoutAnId()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "settings-ann@example.com");

        var list = await client.GetFromJsonAsync<List<DayOffResponse>>($"{Url}?year=2026", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(list!, d => d.Type == "PublicHoliday" && d.Id is null && d.Date == new DateOnly(2026, 7, 21));
    }

    [Fact]
    public async Task OneCompanyDayOffPerDate()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "settings-ann@example.com");
        var token = await TestLogin.ApiTokenAsync(client);
        var request = new DayOffRequest(new(2032, 5, 14), "Bridge day");
        var first = (await (await Send(client, token, HttpMethod.Post, Url, request)).Content.ReadFromJsonAsync<DayOffResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
        var other = (await (await Send(client, token, HttpMethod.Post, Url, request with { Date = new(2032, 5, 15) }))
            .Content.ReadFromJsonAsync<DayOffResponse>(cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, token, HttpMethod.Post, Url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, token, HttpMethod.Put, $"{Url}/{other.Id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, token, HttpMethod.Put, $"{Url}/{first.Id}", request)).StatusCode);
    }

    [Fact]
    public async Task BlankNameIsRefused()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "settings-ann@example.com");
        var token = await TestLogin.ApiTokenAsync(client);

        var response = await Send(client, token, HttpMethod.Post, Url, new DayOffRequest(new(2033, 5, 14), "  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CapacityRulesUseTheSavedCompanyDaysOff()
    {
        var day = new DateOnly(2034, 5, 15); // a Monday, no public holiday
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.CompanyDaysOff.Add(new CompanyDayOff { Date = day, Name = "Closure" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var read = _factory.Services.CreateScope();
        var companyDays = await read.ServiceProvider.GetRequiredService<HeadroomDbContext>().CompanyDaysOffAsync(day, day, ct: TestContext.Current.CancellationToken);
        var holidays = _factory.Services.GetRequiredService<IPublicHolidays>();
        var contract = new ContractTerm(new(2034, 1, 1), new Dictionary<DayOfWeek, decimal> { [DayOfWeek.Monday] = 8 });

        Assert.Equal(0, CapacityRules.Day(day, new PersonRecords([contract], holidays, companyDays, [])).Capacity);
        Assert.Equal(8, CapacityRules.Day(day, new PersonRecords([contract], holidays, new HashSet<DateOnly>(), [])).Capacity);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string token, HttpMethod method, string url, DayOffRequest? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}

public class DayOffNotesTests
{
    private static readonly DayOffResponse Holiday = new("PublicHoliday", null, new(2026, 7, 21), "Nationale feestdag");

    [Fact]
    public void WarnsWhenTheDateIsAPublicHoliday() =>
        Assert.Contains("no effect", DayOffNotes.ForCompanyDay(new(2026, 7, 21), [Holiday]));

    [Fact]
    public void WarnsWhenTheDateIsAWeekend() =>
        Assert.Contains("Weekend", DayOffNotes.ForCompanyDay(new(2026, 7, 25), [Holiday]));

    [Fact]
    public void NoNoteForAWeekday() => Assert.Null(DayOffNotes.ForCompanyDay(new(2026, 7, 22), [Holiday]));

    [Fact]
    public void PublicHolidayOnASundayOffersAReplacementDay() =>
        Assert.Contains("replacement day", DayOffNotes.ForPublicHoliday(new(2026, 11, 1)));

    [Fact]
    public void PublicHolidayOnAWeekdayHasNoNote() => Assert.Null(DayOffNotes.ForPublicHoliday(new(2026, 7, 21)));
}
