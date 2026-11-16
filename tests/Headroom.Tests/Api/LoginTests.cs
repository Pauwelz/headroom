using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class LoginTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;

    public LoginTests(ClockedHeadroomApp app)
    {
        _factory = app.Factory;
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
        foreach (var name in new[] { "Flo Good", "Gil Pages" })
            db.People.Add(new Person { Name = name, Email = $"{name.Split(' ')[0].ToLowerInvariant()}@example.com", LastChangedBy = ann.Id });
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

        // The email matches without case.
        client = await TestLogin.SignInAsync(_factory, "FLO@example.com");

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
        var client = await TestLogin.SignInAsync(_factory, "gil@example.com");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/schedule", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ErrorPageNeedsNoSession()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewClient().GetAsync("/Error", TestContext.Current.CancellationToken)).StatusCode);
    }

    private HttpClient NewClient() => TestLogin.NewClient(_factory);
}
