using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class LoginTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail;
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;

    public LoginTests(ClockedHeadroomApp app)
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
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann Planner", Email = "ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme <Co>", LastChangedBy = ann.Id });
        db.People.Add(ann);
        foreach (var (name, end) in new[] { ("Bob Leaver", new DateOnly(2026, 11, 1)), ("Cy Rate", (DateOnly?)null), ("Di Wrong", null), ("Ed Expired", null), ("Flo Good", null), ("Gil Pages", null) })
            db.People.Add(new Person { Name = name, Email = $"{name.Split(' ')[0].ToLowerInvariant()}@example.com", EndDate = end, LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GoodLoginSignsInAndApiMeReturnsThePerson()
    {
        var client = NewClient();
        var response = await client.GetAsync("/login", TestContext.Current.CancellationToken);
        await PagePolicy.AssertAsync(response);
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Privacy notice", page);
        Assert.Contains("stored Webhook Events", page);
        Assert.Contains("Acme &lt;Co&gt; is the controller", page);

        var code = await RequestCode(client, "FLO@example.com", "flo@example.com");
        var done = await Post(client, "/login/verify", Token(await client.GetStringAsync("/login", TestContext.Current.CancellationToken)),
            ("email", "FLO@example.com"), ("code", code));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);

        var me = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(("Person", "Flo Good", "flo@example.com", false), (body!.Type, body.Name, body.Email, body.IsPlanner));

        // A write without the antiforgery header is refused; with the header it passes the check.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/me", null, TestContext.Current.CancellationToken)).StatusCode);
        var xsrf = Regex.Match(string.Join(';', me.Headers.GetValues("Set-Cookie")), "XSRF-TOKEN=([^;]+)").Groups[1].Value;
        var withToken = new HttpRequestMessage(HttpMethod.Post, "/api/me");
        withToken.Headers.Add("X-XSRF-TOKEN", WebUtility.UrlDecode(xsrf));
        Assert.NotEqual(HttpStatusCode.BadRequest, (await client.SendAsync(withToken, TestContext.Current.CancellationToken)).StatusCode); // no write endpoint exists yet
    }

    [Fact]
    public async Task ApiMeNeedsALogin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    // The server redirects before it sends the app shell, so a logged-out visitor never sees a loading page.
    [Theory]
    [InlineData("/schedule")]
    [InlineData("/")]
    public async Task PagesRedirectToLoginWithoutASession(string path)
    {
        var response = await NewClient().GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task PagesLoadWithASession()
    {
        // Its own Person: the codes of one email share the fake time, so the newest code is ambiguous.
        var client = await TestLogin.SignInAsync(_factory, _mail, "gil@example.com");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/schedule", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ErrorPageNeedsNoSession()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewClient().GetAsync("/Error", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task WrongCodeIsRefusedAndFiveAttemptsBurnTheCode()
    {
        var client = NewClient();
        var good = await RequestCode(client, "di@example.com", "di@example.com");
        var bad = good == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, "di@example.com", bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, "di@example.com", good)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ExpiredCodeIsRefused()
    {
        var client = NewClient();
        var code = await RequestCode(client, "ed@example.com", "ed@example.com");
        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, "ed@example.com", code)).StatusCode);
    }

    [Fact]
    public async Task UnknownEmailAndLeaverGetTheSamePageAndNoMail()
    {
        var client = NewClient();
        var sentBefore = _mail.Sent.Count;
        var good = await RequestCodePage(client, "ann@example.com");
        Assert.Equal(sentBefore + 1, _mail.Sent.Count);

        var unknown = await RequestCodePage(client, "nobody@example.com");
        var leaver = await RequestCodePage(client, "bob@example.com");
        Assert.Equal(sentBefore + 1, _mail.Sent.Count);
        Assert.Equal(StripEmail(good, "ann@example.com"), StripEmail(unknown, "nobody@example.com"));
        Assert.Equal(StripEmail(good, "ann@example.com"), StripEmail(leaver, "bob@example.com"));
    }

    [Fact]
    public async Task FourthCodeRequestInFifteenMinutesSendsNoMailButShowsTheSamePage()
    {
        var client = NewClient();
        var before = _mail.Sent.Count;
        var pages = new List<string>();
        for (var i = 0; i < 4; i++) pages.Add(await RequestCodePage(client, "cy@example.com"));
        Assert.Equal(before + 3, _mail.Sent.Count);
        Assert.All(pages, p => Assert.Equal(StripEmail(pages[0], "cy@example.com"), StripEmail(p, "cy@example.com")));

        _time.Advance(TimeSpan.FromMinutes(16));
        await RequestCodePage(client, "cy@example.com");
        Assert.Equal(before + 4, _mail.Sent.Count);
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure
    });

    private async Task<string> RequestCodePage(HttpClient client, string email)
    {
        var token = Token(await client.GetStringAsync("/login"));
        var response = await Post(client, "/login", token, ("email", email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> RequestCode(HttpClient client, string typed, string stored)
    {
        await RequestCodePage(client, typed);
        return _mail.Sent.Last(m => m.To == stored).Code;
    }

    private async Task<HttpResponseMessage> Verify(HttpClient client, string email, string code) =>
        await Post(client, "/login/verify", Token(await client.GetStringAsync("/login")), ("email", email), ("code", code));

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string token, params (string, string)[] fields) =>
        client.PostAsync(url, new FormUrlEncodedContent(
            fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2)).Append(KeyValuePair.Create("__RequestVerificationToken", token))));

    // The antiforgery token and the echoed email differ per request; the rest of the page must match.
    private static string StripEmail(string html, string email) =>
        Regex.Replace(html.Replace(WebUtility.HtmlEncode(email), "EMAIL"), "name=\"__RequestVerificationToken\" value=\"[^\"]+\"", "TOKEN");

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
}
