using System.Net;
using System.Net.Http.Json;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// A HeadroomApp with a fake provider behind the Company login and a clock that tests control.
public class CompanyLoginApp : HeadroomApp
{
    public FakeProvider Provider { get; } = new();

    // The clock starts at the real time: the test client drops cookies that the app dates in the past.
    public DateTimeOffset Start { get; } = DateTimeOffset.UtcNow;

    public TestClock Time { get; }

    public CompanyLoginApp() => Time = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.AddSingleton<TimeProvider>(Time);
        services.PostConfigure<OpenIdConnectOptions>(CompanyLogin.Scheme, Provider.Configure);
    });
}

public class CompanyLoginTests : IClassFixture<CompanyLoginApp>, IAsyncLifetime
{
    private const string Planner = "cl-planner@example.com";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string EntraIssuer = $"https://login.microsoftonline.com/{Tenant}/v2.0";
    private const string OtherIssuer = "https://idp.example.com";
    private const string Secret = "client-secret-value";

    private readonly CompanyLoginApp _app;
    private readonly WebApplicationFactory<Program> _factory;

    public CompanyLoginTests(CompanyLoginApp app)
    {
        _app = app;
        _factory = app.Factory;
        app.Time.Set(app.Start);
        app.Provider.RefuseCode = false;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!db.People.Any())
        {
            var planner = new Person { Name = "Pat Planner", Email = Planner, IsPlanner = true };
            planner.LastChangedBy = planner.Id;
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = planner.Id });
            db.People.Add(planner);
            await db.SaveChangesAsync();
        }
        // Every test starts with a Company login of the Entra kind, and a Planner without an identifier.
        await SetCompanyLoginAsync(CompanyLoginKind.Entra, EntraIssuer);
        await db.People.Where(p => p.Email == Planner).ExecuteUpdateAsync(p => p.SetProperty(x => x.CompanyLoginIssuer, (string?)null).SetProperty(x => x.CompanyLoginSubject, (string?)null));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task SetCompanyLoginAsync(CompanyLoginKind? kind, string? issuer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var secret = kind is null ? null : CompanyLogin.ProtectSecret(Secret, protection);
        await db.InstanceSettings.ExecuteUpdateAsync(s => s.SetProperty(i => i.CompanyLoginKind, kind).SetProperty(i => i.CompanyLoginIssuer, issuer)
            .SetProperty(i => i.CompanyLoginClientId, kind == null ? null : "headroom").SetProperty(i => i.CompanyLoginClientSecret, secret));
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private async Task<Person> AddPersonAsync(string email, DateOnly? end = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var person = new Person { Name = "Test Person", Email = email, EndDate = end };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    private async Task<Person> ReloadAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.AsNoTracking().SingleAsync(p => p.Email == email);
    }

    private static string NewEmail() => $"cl-{Guid.NewGuid():N}@example.com";

    // The provider claims of a login. "id" is the object id (Entra) or the subject (Other).
    private static (string, string)[] Claims(CompanyLoginKind kind, string id, string email) => kind == CompanyLoginKind.Entra
        ? [("sub", "entra-" + id), ("tid", Tenant), ("oid", id), ("preferred_username", email), ("name", "Test Person")]
        : [("sub", id), ("email", email), ("email_verified", "true"), ("name", "Test Person")];

    private async Task UseKindAsync(CompanyLoginKind kind) =>
        await SetCompanyLoginAsync(kind, kind == CompanyLoginKind.Entra ? EntraIssuer : OtherIssuer);

    private static (string Issuer, string Subject) Identifier(CompanyLoginKind kind, string id) =>
        kind == CompanyLoginKind.Entra ? (Tenant, id) : (OtherIssuer, id);

    private static async Task<HttpStatusCode> MeAsync(HttpClient client) =>
        (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode;

    private static void AssertRefused(HttpResponseMessage callback) =>
        Assert.Equal("/login?companyLogin=refused", callback.Headers.Location!.OriginalString);

    [Theory]
    [InlineData(CompanyLoginKind.Entra)]
    [InlineData(CompanyLoginKind.Other)]
    public async Task TheFirstLoginMatchesTheEmailAndStoresTheIdentifier(CompanyLoginKind kind)
    {
        await UseKindAsync(kind);
        var email = NewEmail();
        await AddPersonAsync(email);
        var client = NewClient();

        var callback = await _app.Provider.LoginAsync(client, "/login/company", Claims(kind, "id-1", email));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client));
        var person = await ReloadAsync(email);
        Assert.Equal(Identifier(kind, "id-1"), (person.CompanyLoginIssuer!, person.CompanyLoginSubject!));
        // The login uses the authorization code flow with PKCE and the client secret.
        Assert.Equal(("headroom", Secret), (_app.Provider.LastTokenRequest["client_id"], _app.Provider.LastTokenRequest["client_secret"]));
        Assert.True(_app.Provider.LastTokenRequest.ContainsKey("code_verifier"));
    }

    [Theory]
    [InlineData(CompanyLoginKind.Entra)]
    [InlineData(CompanyLoginKind.Other)]
    public async Task ALaterLoginMatchesTheIdentifierEvenWhenTheEmailChanged(CompanyLoginKind kind)
    {
        await UseKindAsync(kind);
        var email = NewEmail();
        await AddPersonAsync(email);
        await _app.Provider.LoginAsync(NewClient(), "/login/company", Claims(kind, "id-2", email));

        var client = NewClient();
        var callback = await _app.Provider.LoginAsync(client, "/login/company", Claims(kind, "id-2", NewEmail()));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client));
    }

    [Theory]
    [InlineData(CompanyLoginKind.Entra)]
    [InlineData(CompanyLoginKind.Other)]
    public async Task ASecondIdentifierWithTheSameEmailDoesNotTakeOverThePerson(CompanyLoginKind kind)
    {
        await UseKindAsync(kind);
        var email = NewEmail();
        await AddPersonAsync(email);
        await _app.Provider.LoginAsync(NewClient(), "/login/company", Claims(kind, "id-3", email));

        var client = NewClient();
        var callback = await _app.Provider.LoginAsync(client, "/login/company", Claims(kind, "id-3-other", email));

        AssertRefused(callback);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
        Assert.Equal(Identifier(kind, "id-3").Subject, (await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Theory]
    [InlineData(CompanyLoginKind.Entra)]
    [InlineData(CompanyLoginKind.Other)]
    public async Task AnUnknownLoginIsRefused(CompanyLoginKind kind)
    {
        await UseKindAsync(kind);
        var client = NewClient();

        AssertRefused(await _app.Provider.LoginAsync(client, "/login/company", Claims(kind, "id-4", NewEmail())));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
    }

    [Theory]
    [InlineData(CompanyLoginKind.Entra)]
    [InlineData(CompanyLoginKind.Other)]
    public async Task APersonPastTheEndDateIsRefusedAndKeepsNoIdentifier(CompanyLoginKind kind)
    {
        await UseKindAsync(kind);
        var email = NewEmail();
        await AddPersonAsync(email, end: DateOnly.FromDateTime(_app.Start.UtcDateTime).AddDays(-2));
        var client = NewClient();

        AssertRefused(await _app.Provider.LoginAsync(client, "/login/company", Claims(kind, "id-5", email)));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public async Task OtherNeedsAVerifiedEmail(string? verified)
    {
        await UseKindAsync(CompanyLoginKind.Other);
        var email = NewEmail();
        await AddPersonAsync(email);
        var claims = new List<(string, string)> { ("sub", "id-6"), ("email", email) };
        if (verified is not null) claims.Add(("email_verified", verified));

        AssertRefused(await _app.Provider.LoginAsync(NewClient(), "/login/company", [.. claims]));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task EntraIgnoresTheEmailClaim()
    {
        var email = NewEmail();
        await AddPersonAsync(email);

        AssertRefused(await _app.Provider.LoginAsync(NewClient(), "/login/company",
            ("sub", "s7"), ("tid", Tenant), ("oid", "id-7"), ("preferred_username", NewEmail()), ("email", email), ("email_verified", "true")));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task AProviderErrorAtTheCallbackGivesTheProviderOutcome()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        _app.Provider.RefuseCode = true;

        var callback = await _app.Provider.LoginAsync(NewClient(), "/login/company", Claims(CompanyLoginKind.Entra, "id-8", email));

        Assert.Equal("/login?companyLogin=provider", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheLoginPageShowsTheButtonOnlyWithACompanyLogin()
    {
        var client = NewClient();
        var with = await client.GetStringAsync("/login", TestContext.Current.CancellationToken);
        Assert.Contains("Company login</button>", with);
        Assert.True(with.IndexOf("Company login</button>") < with.IndexOf("name=\"email\""));

        await SetCompanyLoginAsync(null, null);
        Assert.DoesNotContain("Company login", await client.GetStringAsync("/login", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/login/company", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ACompanyLoginSessionEndsAfter12HoursButALoginCodeSessionDoesNot()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        var company = NewClient();
        await _app.Provider.LoginAsync(company, "/login/company", Claims(CompanyLoginKind.Entra, "id-9", email));
        var codeEmail = NewEmail();
        await AddPersonAsync(codeEmail);
        var code = await TestLogin.SignInAsync(_factory, _app.Mail, codeEmail);

        _app.Time.Advance(TimeSpan.FromHours(11));
        // Activity does not slide the Company login session.
        Assert.Equal(HttpStatusCode.OK, await MeAsync(company));
        _app.Time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(company));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(code));
    }

    [Fact]
    public async Task AMultiTenantEntraIssuerIsRefusedAndNothingIsStored()
    {
        var (client, token) = await PlannerAsync();

        foreach (var tenant in new[] { "common", "organizations", "consumers" })
        {
            var response = await PutAsync(client, token, new(CompanyLoginKind.Entra, $"https://login.microsoftonline.com/{tenant}/v2.0", "id", Secret));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(EntraIssuer, (await GetSettingsAsync(client)).Issuer);
    }

    [Fact]
    public async Task TestAndSaveKeepsNewSettingsOnlyAfterAMatchingTestLogin()
    {
        var (client, token) = await PlannerAsync();
        await UseKindAsync(CompanyLoginKind.Entra);

        var put = await PutAsync(client, token, new(CompanyLoginKind.Other, OtherIssuer, "new-client", "new-secret"));
        var test = (await put.Content.ReadFromJsonAsync<CompanyLoginTestResponse>(TestContext.Current.CancellationToken))!;
        // Nothing is stored before the test login.
        Assert.Equal(CompanyLoginKind.Entra, (await GetSettingsAsync(client)).Kind);
        var callback = await _app.Provider.LoginAsync(client, test.TestUrl, Claims(CompanyLoginKind.Other, "planner-sub", Planner));

        Assert.Equal("/settings?companyLogin=saved", callback.Headers.Location!.OriginalString);
        var saved = await GetSettingsAsync(client);
        Assert.Equal((CompanyLoginKind.Other, OtherIssuer, "new-client", true), (saved.Kind, saved.Issuer, saved.ClientId, saved.HasSecret));
        Assert.Equal(("new-client", "new-secret"), (_app.Provider.LastTokenRequest["client_id"], _app.Provider.LastTokenRequest["client_secret"]));
        // The secret is stored as ciphertext and no API returns it.
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("new-secret", row.CompanyLoginClientSecret);
        Assert.DoesNotContain("new-secret", await (await client.GetAsync("/api/settings/company-login", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AProviderErrorInTheTestLoginKeepsTheOldSettings()
    {
        var (client, token) = await PlannerAsync();
        var put = await PutAsync(client, token, new(CompanyLoginKind.Other, OtherIssuer, "new-client", "new-secret"));
        var test = (await put.Content.ReadFromJsonAsync<CompanyLoginTestResponse>(TestContext.Current.CancellationToken))!;
        _app.Provider.RefuseCode = true;

        var callback = await _app.Provider.LoginAsync(client, test.TestUrl, Claims(CompanyLoginKind.Other, "planner-sub", Planner));

        Assert.Equal("/settings?companyLogin=provider", callback.Headers.Location!.OriginalString);
        Assert.Equal((CompanyLoginKind.Entra, EntraIssuer), ((await GetSettingsAsync(client)).Kind, (await GetSettingsAsync(client)).Issuer));
    }

    [Fact]
    public async Task ATestLoginOfAnotherPersonKeepsTheOldSettings()
    {
        var (client, token) = await PlannerAsync();
        var other = NewEmail();
        await AddPersonAsync(other);
        var put = await PutAsync(client, token, new(CompanyLoginKind.Other, OtherIssuer, "new-client", "new-secret"));
        var test = (await put.Content.ReadFromJsonAsync<CompanyLoginTestResponse>(TestContext.Current.CancellationToken))!;

        var callback = await _app.Provider.LoginAsync(client, test.TestUrl, Claims(CompanyLoginKind.Other, "other-sub", other));

        Assert.Equal("/settings?companyLogin=not-you", callback.Headers.Location!.OriginalString);
        Assert.Equal(CompanyLoginKind.Entra, (await GetSettingsAsync(client)).Kind);
        Assert.Null((await ReloadAsync(other)).CompanyLoginSubject); // the other Person gets no identifier either
    }

    [Fact]
    public async Task ARemovedCompanyLoginClearsTheSettingsAndTheIdentifiers()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        await _app.Provider.LoginAsync(NewClient(), "/login/company", Claims(CompanyLoginKind.Entra, "id-10", email));
        var (client, token) = await PlannerAsync();

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/settings/company-login");
        delete.Headers.Add(AntiforgeryToken.HeaderName, token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete, TestContext.Current.CancellationToken)).StatusCode);

        var settings = await GetSettingsAsync(client);
        Assert.Equal((null, false), (settings.Kind, settings.HasSecret));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    private async Task<(HttpClient, string)> PlannerAsync()
    {
        var client = await TestLogin.SignInAsync(_factory, _app.Mail, Planner);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string token, CompanyLoginRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings/company-login") { Content = JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryToken.HeaderName, token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<CompanyLoginResponse> GetSettingsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CompanyLoginResponse>("/api/settings/company-login", TestContext.Current.CancellationToken))!;
}
