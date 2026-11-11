using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Company login is the only login for the company domains (ticket #86).
public class CompanyLoginOnlyTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/company-login-only";
    private const string Domain = "corp.example";
    private const string Planner = "co-ann@boss.example";
    private const string Freelancer = "co-fred@free.example";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CompanyLoginOnlyTests(ClockedHeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
        app.Time.Set(ClockedHeadroomApp.Start);
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!db.People.Any())
        {
            var ann = new Person { Name = "Ann", Email = Planner, IsPlanner = true, CompanyLoginIssuer = "https://idp.example.com", CompanyLoginSubject = "ann" };
            ann.LastChangedBy = ann.Id;
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.Add(ann);
            db.People.Add(new Person { Name = "Fred", Email = Freelancer, LastChangedBy = ann.Id });
            await db.SaveChangesAsync(Ct);
        }
        // Every test starts with a Company login, the switch off and no domains.
        await SetAsync(kind: CompanyLoginKind.Other, on: false);
        await db.People.Where(p => p.Email == Planner).ExecuteUpdateAsync(p => p.SetProperty(x => x.CompanyLoginSubject, "ann"), Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SetAsync(CompanyLoginKind? kind, bool on, params string[] domains)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var row = await db.InstanceSettings.FirstAsync(Ct);
        (row.CompanyLoginKind, row.CompanyLoginOnly, row.CompanyDomains) = (kind, on, domains);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<string> AddPersonAsync(string name, string domain)
    {
        var email = $"co-{name}-{Guid.NewGuid():N}@{domain}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        db.People.Add(new Person { Name = name, Email = email });
        await db.SaveChangesAsync(Ct);
        return email;
    }

    private async Task<bool> HasPasskeyAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        return await db.Passkeys.AnyAsync(k => db.People.Any(p => p.Id == k.PersonId && p.Email == email), Ct);
    }

    private async Task<(HttpClient Client, string Xsrf)> SignInAsync(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string xsrf, HttpMethod method, string url, string? json)
    {
        var request = new HttpRequestMessage(method, url) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(AntiforgeryToken.HeaderName, xsrf);
        return await client.SendAsync(request, Ct);
    }

    private static string Body(bool on, params string[] domains) =>
        System.Text.Json.JsonSerializer.Serialize(new CompanyLoginOnlyRequest(on, [.. domains]));

    private async Task<HttpResponseMessage> TurnOnAsync(params string[] domains)
    {
        var (planner, xsrf) = await SignInAsync(Planner);
        return await Send(planner, xsrf, HttpMethod.Put, Url, Body(true, domains));
    }

    // Saves a Passkey for the email through the Person's own endpoints. The switch must be off.
    private async Task<TestAuthenticator> SavePasskeyAsync(string email)
    {
        var (client, xsrf) = await SignInAsync(email);
        var authenticator = new TestAuthenticator();
        var options = await Send(client, xsrf, HttpMethod.Post, "/api/me/passkeys/options", null);
        var saved = await Send(client, xsrf, HttpMethod.Post, "/api/me/passkeys", authenticator.CreateAttestation(await options.Content.ReadAsStringAsync(Ct)));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        return authenticator;
    }

    private HttpClient Anonymous() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> PasskeyLoginAsync(HttpClient client, TestAuthenticator authenticator)
    {
        var options = await client.PostAsync("/login/passkey/options", null, Ct);
        return await client.PostAsync("/login/passkey", new StringContent(authenticator.CreateAssertion(await options.Content.ReadAsStringAsync(Ct)), Encoding.UTF8, "application/json"), Ct);
    }

    [Theory]
    [InlineData("a@corp.example", true)]
    [InlineData("a@CORP.Example", true)]
    [InlineData("a@mail.corp.example", false)]
    [InlineData("a@notcorp.example", false)]
    [InlineData("a@corp.example.org", false)]
    [InlineData("a@*.example", false)]
    public void ADomainMatchesExactlyWithoutCase(string email, bool expected)
    {
        var settings = new InstanceSettings { CompanyLoginOnly = true, CompanyDomains = [Domain] };

        Assert.Equal(expected, CompanyLoginOnly.Match(settings, email) is not null);
    }

    [Fact]
    public void NothingMatchesWhileTheSwitchIsOff() =>
        Assert.Null(CompanyLoginOnly.Match(new InstanceSettings { CompanyLoginOnly = false, CompanyDomains = [Domain] }, "a@corp.example"));

    [Fact]
    public async Task NoLoginCodeIsSentForACompanyDomainAndTheReplyIsTheSameForAKnownAndAnUnknownEmail()
    {
        var known = await AddPersonAsync("known", Domain);
        var unknown = $"co-nobody-{Guid.NewGuid():N}@{Domain.ToUpperInvariant()}";
        await SetAsync(CompanyLoginKind.Other, true, Domain);
        var client = Anonymous();

        var knownReply = await TestLogin.Post(client, "/login", ("email", known));
        var unknownReply = await TestLogin.Post(client, "/login", ("email", unknown));

        Assert.Equal(HttpStatusCode.OK, knownReply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownReply.StatusCode);
        Assert.Contains("Use Company login for @corp.example", await knownReply.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Use Company login for @corp.example", await unknownReply.Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain(_mail.Sent, m => m.To == known || m.To == unknown);
    }

    [Fact]
    public async Task AnotherDomainStillGetsALoginCodeAndAPasskeyLogin()
    {
        using var authenticator = await SavePasskeyAsync(Freelancer);
        await SetAsync(CompanyLoginKind.Other, true, Domain);

        await SignInAsync(Freelancer);
        var login = await PasskeyLoginAsync(Anonymous(), authenticator);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task APasskeyLoginIsRefusedForACompanyDomain()
    {
        var email = await AddPersonAsync("pk", Domain);
        using var authenticator = await SavePasskeyAsync(email);
        await SetAsync(CompanyLoginKind.Other, true, Domain); // no deletion: only the login rule is under test

        var login = await PasskeyLoginAsync(Anonymous(), authenticator);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task ACodeThatWasSentBeforeTheSwitchDoesNotLogIn()
    {
        var email = await AddPersonAsync("code", Domain);
        var client = Anonymous();
        await TestLogin.Post(client, "/login", ("email", email));
        var code = _mail.Sent.Last(m => m.To == email).Code;
        await SetAsync(CompanyLoginKind.Other, true, Domain);

        var done = await TestLogin.Post(client, "/login/verify", ("email", email), ("code", code));

        Assert.Equal(HttpStatusCode.BadRequest, done.StatusCode);
    }

    [Fact]
    public async Task APasskeyCannotBeRegisteredForACompanyDomain()
    {
        var email = await AddPersonAsync("reg", Domain);
        var (client, xsrf) = await SignInAsync(email);
        await SetAsync(CompanyLoginKind.Other, true, Domain);

        var options = await Send(client, xsrf, HttpMethod.Post, "/api/me/passkeys/options", null);
        var saved = await Send(client, xsrf, HttpMethod.Post, "/api/me/passkeys", "{}");

        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saved.StatusCode);
    }

    [Fact]
    public async Task TurningTheSwitchOnDeletesThePasskeysOfAffectedPeopleOnlyAndAddingADomainDeletesMore()
    {
        var corp = await AddPersonAsync("corp", Domain);
        var other = await AddPersonAsync("other", "other.example");
        using var corpKey = await SavePasskeyAsync(corp);
        using var otherKey = await SavePasskeyAsync(other);

        Assert.Equal(HttpStatusCode.NoContent, (await TurnOnAsync(Domain)).StatusCode);

        Assert.False(await HasPasskeyAsync(corp));
        Assert.True(await HasPasskeyAsync(other));

        var (planner, xsrf) = await SignInAsync(Planner);
        var added = await Send(planner, xsrf, HttpMethod.Put, Url, Body(true, Domain, "other.example"));
        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.False(await HasPasskeyAsync(other));
    }

    [Fact]
    public async Task ChangingAnEmailIntoACompanyDomainDeletesThePasskeys()
    {
        var email = await AddPersonAsync("move", "other.example");
        using var key = await SavePasskeyAsync(email);
        var (planner, xsrf) = await SignInAsync(Planner);
        await Send(planner, xsrf, HttpMethod.Put, Url, Body(true, Domain));
        using var scope = _factory.Services.CreateScope();
        var id = (await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.SingleAsync(p => p.Email == email, Ct)).Id;
        Assert.True(await HasPasskeyAsync(email));

        var (planner2, xsrf2) = await SignInAsync(Planner);
        var edit = await Send(planner2, xsrf2, HttpMethod.Put, $"/api/people/{id}", $$"""{"name":"move","email":"co-moved-{{Guid.NewGuid():N}}@corp.example","isPlanner":false}""");

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.False(await HasPasskeyAsync(email) || await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Passkeys.AnyAsync(k => k.PersonId == id, Ct));
    }

    [Fact]
    public async Task TurningTheSwitchOffRestoresTheLoginCode()
    {
        var email = await AddPersonAsync("back", Domain);
        await SetAsync(CompanyLoginKind.Other, true, Domain);
        await SetAsync(CompanyLoginKind.Other, false, Domain);

        await TestLogin.Post(Anonymous(), "/login", ("email", email));

        Assert.Contains(_mail.Sent, m => m.To == email);
    }

    [Fact]
    public async Task TheSwitchIsRefusedWithoutACompanyLogin()
    {
        var (planner, xsrf) = await SignInAsync(Planner);
        await SetAsync(kind: null, on: false);

        var response = await Send(planner, xsrf, HttpMethod.Put, Url, Body(true, Domain));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Set up Company login first", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task TheSwitchIsRefusedForAPlannerWhoNeverLoggedInWithCompanyLogin()
    {
        var (planner, xsrf) = await SignInAsync(Planner);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Where(p => p.Email == Planner)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.CompanyLoginSubject, (string?)null), Ct);

        var response = await Send(planner, xsrf, HttpMethod.Put, Url, Body(true, Domain));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Log in with Company login once", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ADomainWithAWildcardOrAnAtSignIsRefused()
    {
        var (planner, xsrf) = await SignInAsync(Planner);

        var wildcard = await Send(planner, xsrf, HttpMethod.Put, Url, Body(false, "*.corp.example"));
        var cleaned = await Send(planner, xsrf, HttpMethod.Put, Url, Body(false, " @Corp.Example "));

        Assert.Equal(HttpStatusCode.BadRequest, wildcard.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cleaned.StatusCode);
        Assert.Equal([Domain], (await planner.GetFromJsonAsync<CompanyLoginOnlyResponse>(Url, Ct))!.Domains);
    }

    [Fact]
    public async Task AnAccessTokenOfAnAffectedPersonStillWorks()
    {
        var email = await AddPersonAsync("token", Domain);
        var (client, xsrf) = await SignInAsync(email);
        var created = await Send(client, xsrf, HttpMethod.Post, "/api/me/access-tokens", """{"name":"Script","canChange":false}""");
        var secret = (await created.Content.ReadFromJsonAsync<NewAccessTokenResponse>(Ct))!.Secret;
        await SetAsync(CompanyLoginKind.Other, true, Domain);
        var bearer = Anonymous();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/me", Ct)).StatusCode);
    }
}
