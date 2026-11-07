using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Log in with a Passkey (ticket #41). The test authenticator signs as a browser would.
public class PasskeyLoginTests : IClassFixture<ClockedHeadroomApp>, IAsyncLifetime
{
    private const string Refusal = "Headroom could not log you in with this Passkey. Use a Login code instead.";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PasskeyLoginTests(ClockedHeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
        app.Time.Set(ClockedHeadroomApp.Start);
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann", Email = "pl-ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        foreach (var name in new[] { "Bea", "Cal", "Dan", "Eve", "Fay", "Gil", "Hal" })
            db.People.Add(new Person { Name = name, Email = $"pl-{name.ToLowerInvariant()}@example.com", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private HttpClient Anonymous() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"), // the cookies are Secure
    });

    // Saves a Passkey for the Person through the Account endpoints, as a Person does in the app.
    private async Task<TestAuthenticator> SaveAsync(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        var xsrf = await TestLogin.ApiTokenAsync(client);
        var authenticator = new TestAuthenticator();
        var options = await Send(client, "/api/me/passkeys/options", null, xsrf);
        var saved = await Send(client, "/api/me/passkeys", authenticator.CreateAttestation(await options.Content.ReadAsStringAsync(Ct)), xsrf);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        return authenticator;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string url, string? json, string? xsrf = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
        if (xsrf is not null) request.Headers.Add(AntiforgeryToken.HeaderName, xsrf);
        return client.SendAsync(request, Ct);
    }

    // Runs the login ceremony on a client without a session.
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, Func<string, string> respond, string query = "")
    {
        var options = await Send(client, "/login/passkey/options", null);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        return await Send(client, "/login/passkey" + query, respond(await options.Content.ReadAsStringAsync(Ct)));
    }

    private async Task<string> MeEmailAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeResponse>("/api/me", Ct))!.Email!;

    private async Task<Passkey> StoredAsync(TestAuthenticator authenticator)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Passkeys.AsNoTracking()
            .SingleAsync(p => p.CredentialId == authenticator.CredentialId, Ct);
    }

    [Fact]
    public async Task OptionsHaveAnEmptyAllowList()
    {
        var response = await Send(Anonymous(), "/login/passkey/options", null);

        using var options = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, options.RootElement.GetProperty("allowCredentials").GetArrayLength());
        Assert.Equal("localhost", options.RootElement.GetProperty("rpId").GetString());
    }

    [Fact]
    public async Task APasskeySignsThePersonInAndSavesTheCounterAndTheLastUsedDate()
    {
        using var authenticator = await SaveAsync("pl-ann@example.com");
        var client = Anonymous();

        var response = await LoginAsync(client, o => authenticator.CreateAssertion(o));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("redirect").GetString());
        Assert.Equal("pl-ann@example.com", await MeEmailAsync(client));
        var passkey = await StoredAsync(authenticator);
        Assert.Equal(1, passkey.SignCount);
        Assert.NotNull(passkey.LastUsedAt);
    }

    [Fact]
    public async Task APasskeyLoginDeletesAStalePasskeyOfferMarker()
    {
        using var authenticator = await SaveAsync("pl-cal@example.com");

        var response = await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o));

        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c =>
            c.StartsWith("headroom-passkey-offer=;", StringComparison.Ordinal) && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASafeReturnUrlIsKeptAndAnotherOneIsDropped()
    {
        using var authenticator = await SaveAsync("pl-bea@example.com");
        const string authorize = "/connect/authorize?client_id=x&state=a";

        var kept = await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, 1), "?returnUrl=" + Uri.EscapeDataString(authorize));
        var dropped = await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, 2), "?returnUrl=" + Uri.EscapeDataString("https://evil.example"));

        Assert.Equal(authorize, (await kept.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("redirect").GetString());
        Assert.Equal("/", (await dropped.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("redirect").GetString());
    }

    [Fact]
    public async Task APersonWhoCannotLogInAnymoreIsRefused()
    {
        using var authenticator = await SaveAsync("pl-cal@example.com");
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Where(p => p.Email == "pl-cal@example.com")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndDate, new DateOnly(2026, 11, 1)), Ct);
        var client = Anonymous();

        var response = await LoginAsync(client, o => authenticator.CreateAssertion(o));

        await AssertRefusedAsync(response);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Null((await StoredAsync(authenticator)).LastUsedAt);
    }

    [Fact]
    public async Task AnUnknownCredentialIsRefusedWithTheSameMessage()
    {
        using var authenticator = await SaveAsync("pl-dan@example.com");
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Passkeys.ExecuteDeleteAsync(Ct);

        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o)));
    }

    [Fact]
    public async Task ABadSignatureIsRefused()
    {
        using var authenticator = await SaveAsync("pl-eve@example.com");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, signWith: other)));
    }

    [Fact]
    public async Task AWrongOriginIsRefused()
    {
        using var authenticator = await SaveAsync("pl-fay@example.com");

        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, forgedOrigin: "https://evil.example")));
    }

    [Fact]
    public async Task ACounterThatDoesNotIncreaseIsRefused()
    {
        using var authenticator = await SaveAsync("pl-gil@example.com");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, 5))).StatusCode);

        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, 5)));
        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, 3)));
        Assert.Equal(5, (await StoredAsync(authenticator)).SignCount);
    }

    [Fact]
    public async Task AResponseWithoutAUserHandleIsRefused()
    {
        using var authenticator = await SaveAsync("pl-hal@example.com");

        await AssertRefusedAsync(await LoginAsync(Anonymous(), o => authenticator.CreateAssertion(o, sendUserHandle: false)));
    }

    [Fact]
    public async Task AChallengeWorksOnce()
    {
        using var authenticator = await SaveAsync("pl-ann@example.com");
        var client = Anonymous();
        var options = await (await Send(client, "/login/passkey/options", null)).Content.ReadAsStringAsync(Ct);
        var assertion = authenticator.CreateAssertion(options, 7);

        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/login/passkey", assertion)).StatusCode);
        await AssertRefusedAsync(await Send(client, "/login/passkey", assertion));
    }

    [Fact]
    public async Task OnlyTheLoginPageAllowsItsOwnScript()
    {
        var client = Anonymous();

        var login = await client.GetAsync("/login", Ct);
        await PagePolicy.AssertAsync(login);
        var policy = login.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("connect-src 'self'", policy);
        var html = await login.Content.ReadAsStringAsync(Ct);
        Assert.Contains("""<script src="/passkey-login.js" defer></script>""", html);
        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\bsrc=)"), html);
        Assert.Contains("""autocomplete="username webauthn" """.TrimEnd(), html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/passkey-login.js", Ct)).StatusCode);

        var code = await TestLogin.Post(client, "/login", ("email", "pl-ann@example.com"));
        Assert.DoesNotContain("script-src", code.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Refusal, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
    }
}
