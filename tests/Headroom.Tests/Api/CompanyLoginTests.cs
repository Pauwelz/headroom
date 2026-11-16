using System.Net;
using Headroom.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// A HeadroomApp with a clock that tests control.
public class CompanyLoginApp : HeadroomApp
{
    // The clock starts at the real time: the test client drops cookies that the app dates in the past.
    public DateTimeOffset Start { get; } = DateTimeOffset.UtcNow;

    public TestClock Time { get; }

    public CompanyLoginApp() => Time = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Time));
}

public class CompanyLoginTests : IClassFixture<CompanyLoginApp>, IAsyncLifetime
{
    private const string Planner = "cl-planner@example.com";

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
        if (db.People.Any()) return;
        var planner = new Person { Name = "Pat Planner", Email = Planner, IsPlanner = true };
        planner.LastChangedBy = planner.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = planner.Id });
        db.People.Add(planner);
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient NewClient() => TestLogin.NewClient(_factory);

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

    // The claims of Entra ID: "id" is the object id.
    private static (string, string)[] Claims(string id, string email, string tenant = HeadroomApp.TenantId) =>
        [("sub", "entra-" + id), ("tid", tenant), ("oid", id), ("preferred_username", email), ("name", "Test Person")];

    private Task<HttpResponseMessage> LoginAsync(HttpClient client, params (string, string)[] claims) =>
        _app.Provider.LoginAsync(client, "/login/company", claims);

    private static async Task<HttpStatusCode> MeAsync(HttpClient client) =>
        (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode;

    private static void AssertRefused(HttpResponseMessage callback) =>
        Assert.Equal("/login?companyLogin=refused", callback.Headers.Location!.OriginalString);

    [Fact]
    public async Task TheFirstLoginMatchesTheEmailWithoutCaseAndStoresTheIdentifier()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        var client = NewClient();

        var callback = await LoginAsync(client, Claims("id-1", email.ToUpperInvariant()));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client));
        var person = await ReloadAsync(email);
        Assert.Equal((HeadroomApp.TenantId, "id-1"), (person.CompanyLoginIssuer!, person.CompanyLoginSubject!));
        // The login uses the authorization code flow with PKCE and the client secret.
        Assert.Equal(("headroom", HeadroomApp.ClientSecret), (_app.Provider.LastTokenRequest["client_id"], _app.Provider.LastTokenRequest["client_secret"]));
        Assert.True(_app.Provider.LastTokenRequest.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task ALaterLoginMatchesTheIdentifierEvenWhenTheEmailChanged()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        await LoginAsync(NewClient(), Claims("id-2", email));

        var client = NewClient();
        var callback = await LoginAsync(client, Claims("id-2", NewEmail()));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client));
    }

    [Fact]
    public async Task ASecondIdentifierWithTheSameEmailDoesNotTakeOverThePerson()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        await LoginAsync(NewClient(), Claims("id-3", email));

        var client = NewClient();
        AssertRefused(await LoginAsync(client, Claims("id-3-other", email)));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
        Assert.Equal("id-3", (await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task AnUnknownLoginIsRefused()
    {
        var client = NewClient();

        AssertRefused(await LoginAsync(client, Claims("id-4", NewEmail())));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
    }

    [Fact]
    public async Task APersonPastTheEndDateIsRefusedAndKeepsNoIdentifier()
    {
        var email = NewEmail();
        await AddPersonAsync(email, end: DateOnly.FromDateTime(_app.Start.UtcDateTime).AddDays(-2));
        var client = NewClient();

        AssertRefused(await LoginAsync(client, Claims("id-5", email)));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task ALoginFromAnotherTenantIsRefused()
    {
        var email = NewEmail();
        await AddPersonAsync(email);

        AssertRefused(await LoginAsync(NewClient(), Claims("id-6", email, tenant: "22222222-2222-2222-2222-222222222222")));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task TheEmailClaimIsIgnored()
    {
        var email = NewEmail();
        await AddPersonAsync(email);

        AssertRefused(await LoginAsync(NewClient(),
            ("sub", "s7"), ("tid", HeadroomApp.TenantId), ("oid", "id-7"), ("preferred_username", NewEmail()), ("email", email), ("email_verified", "true")));
        Assert.Null((await ReloadAsync(email)).CompanyLoginSubject);
    }

    [Fact]
    public async Task AProviderErrorAtTheCallbackGivesTheProviderOutcome()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        _app.Provider.RefuseCode = true;

        var callback = await LoginAsync(NewClient(), Claims("id-8", email));

        Assert.Equal("/login?companyLogin=provider", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheLoginPageHasOneButtonAndOneMessageForEveryRefusal()
    {
        var client = NewClient();
        var page = await client.GetStringAsync("/login", TestContext.Current.CancellationToken);
        Assert.Contains("Log in with Microsoft</button>", page);
        Assert.DoesNotContain("name=\"email\"", page);

        var refused = await client.GetStringAsync("/login?companyLogin=refused", TestContext.Current.CancellationToken);
        Assert.Contains("Headroom could not log you in. Ask a Planner for help.", refused);
    }

    [Fact]
    public async Task ASessionEndsAfter12HoursAndActivityDoesNotSlideIt()
    {
        var email = NewEmail();
        await AddPersonAsync(email);
        var client = NewClient();
        await LoginAsync(client, Claims("id-9", email));

        _app.Time.Advance(TimeSpan.FromHours(11));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client));
        _app.Time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client));
    }
}

// The Instance is empty: the first login of the tenant creates the first Planner.
public class CompanyLoginFirstLoginTests(HeadroomApp app) : IClassFixture<HeadroomApp>
{
    [Fact]
    public async Task TheFirstLoginBecomesThePlannerAndASecondPersonIsRefused()
    {
        var first = TestLogin.NewClient(app.Factory);
        var callback = await app.Provider.LoginAsync(first, "/login/company", TestLogin.Claims("first@company.be", "First Planner"));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var person = await db.People.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("First Planner", "first@company.be", true, HeadroomApp.TenantId), (person.Name, person.Email, person.IsPlanner, person.CompanyLoginIssuer));
        Assert.Null((await db.InstanceSettings.SingleAsync(TestContext.Current.CancellationToken)).CompanyName);
        Assert.Equal(1, await db.Contracts.CountAsync(c => c.PersonId == person.Id, TestContext.Current.CancellationToken));

        var second = TestLogin.NewClient(app.Factory);
        var refused = await app.Provider.LoginAsync(second, "/login/company", TestLogin.Claims("second@company.be"));

        Assert.Equal("/login?companyLogin=refused", refused.Headers.Location!.OriginalString);
        Assert.Equal(1, await db.People.CountAsync(TestContext.Current.CancellationToken));
        // The company name is null until a Planner sets it. The pages still render.
        Assert.Contains("Your company is the controller", await second.GetStringAsync("/login", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
    }
}

// No client secret: the code exchange sends a client assertion from the managed identity.
public class NoSecretApp : HeadroomApp
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["EntraId:ClientSecret"] = "" }))
        .ConfigureServices(services => services.AddSingleton<Headroom.Auth.IClientAssertionProvider>(new FakeAssertion()));

    private sealed class FakeAssertion : Headroom.Auth.IClientAssertionProvider
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("assertion-from-managed-identity");
    }
}

public class CompanyLoginAssertionTests(NoSecretApp app) : IClassFixture<NoSecretApp>
{
    [Fact]
    public async Task WithoutASecretTheTokenRequestCarriesTheClientAssertion()
    {
        var callback = await app.Provider.LoginAsync(TestLogin.NewClient(app.Factory), "/login/company", TestLogin.Claims("first@company.be"));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var request = app.Provider.LastTokenRequest;
        Assert.Equal("assertion-from-managed-identity", request["client_assertion"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", request["client_assertion_type"]);
        Assert.False(request.ContainsKey("client_secret"));
    }
}

// The Operator must set the tenant and the client. Without them the app does not start.
public class MissingEntraIdApp : HeadroomApp
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["EntraId:TenantId"] = "not-a-guid", ["EntraId:ClientId"] = "" }));
}

public class EntraIdOptionsTests(MissingEntraIdApp app) : IClassFixture<MissingEntraIdApp>
{
    [Fact]
    public void StartupFailsWhenTheTenantIsNotAGuidOrTheClientIsMissing()
    {
        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => app.Factory.CreateClient());

        Assert.Contains("ClientId", string.Join(' ', error.Failures));
        Assert.Contains("TenantId must be a GUID", string.Join(' ', error.Failures));
    }
}
