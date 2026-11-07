using System.Net;
using System.Net.Http.Json;
using System.Text;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Save, list and remove Passkeys (ticket #40). The test authenticator signs as a browser would.
public class PasskeyTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/me/passkeys";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PasskeyTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann", Email = "pk-ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Flo", Email = "pk-flo@example.com", LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Ivy", Email = "pk-ivy@example.com", LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Jon", Email = "pk-jon@example.com", LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Gus", Email = "pk-gus@example.com", LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Bob", Email = "pk-bob@example.com", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<(HttpClient Client, string Xsrf)> SignInAsync(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private static async Task<string> OptionsAsync(HttpClient client, string xsrf)
    {
        var response = await Send(client, xsrf, HttpMethod.Post, Url + "/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string xsrf, HttpMethod method, string url, string? json = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(AntiforgeryToken.HeaderName, xsrf);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0");
        return client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string xsrf, TestAuthenticator authenticator) =>
        await Send(client, xsrf, HttpMethod.Post, Url, authenticator.CreateAttestation(await OptionsAsync(client, xsrf)));

    [Fact]
    public async Task PersonSavesAPasskeyNamedFromTheBrowserAndSeesItInTheList()
    {
        var (client, xsrf) = await SignInAsync("pk-ann@example.com");
        using var authenticator = new TestAuthenticator();

        var saved = await RegisterAsync(client, xsrf, authenticator);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var list = (await client.GetFromJsonAsync<List<PasskeyResponse>>(Url, Ct))!;
        var passkey = Assert.Single(list);
        Assert.Equal(("Passkey", "Firefox on Linux", null), (passkey.Type, passkey.Name, passkey.LastUsedAt));
    }

    [Fact]
    public async Task CreationOptionsRequireAResidentKeyAskForNoAttestationAndExcludeSavedPasskeys()
    {
        var (client, xsrf) = await SignInAsync("pk-bob@example.com");
        using var first = new TestAuthenticator();
        await RegisterAsync(client, xsrf, first);

        using var options = System.Text.Json.JsonDocument.Parse(await OptionsAsync(client, xsrf));

        var root = options.RootElement;
        Assert.Equal("required", root.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
        Assert.Equal("none", root.GetProperty("attestation").GetString());
        Assert.Equal("localhost", root.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("excludeCredentials").GetArrayLength());
    }

    [Fact]
    public async Task AForgedResponseIsRefusedAndSavesNothing()
    {
        var (client, xsrf) = await SignInAsync("pk-flo@example.com");
        using var authenticator = new TestAuthenticator();

        var forged = authenticator.CreateAttestation(await OptionsAsync(client, xsrf), forgedOrigin: "https://evil.example");

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, xsrf, HttpMethod.Post, Url, forged)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<PasskeyResponse>>(Url, Ct))!);
    }

    [Fact]
    public async Task AChallengeWorksOnce()
    {
        var (client, xsrf) = await SignInAsync("pk-ivy@example.com");
        using var authenticator = new TestAuthenticator();
        var response = authenticator.CreateAttestation(await OptionsAsync(client, xsrf));

        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, xsrf, HttpMethod.Post, Url, response)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, xsrf, HttpMethod.Post, Url, response)).StatusCode);
    }

    [Fact]
    public async Task AResponseWithoutAChallengeIsRefused()
    {
        var (client, xsrf) = await SignInAsync("pk-jon@example.com");
        using var authenticator = new TestAuthenticator();
        var (other, otherXsrf) = await SignInAsync("pk-jon@example.com"); // a new cookie jar: no challenge cookie
        var response = authenticator.CreateAttestation(await OptionsAsync(client, xsrf));

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(other, otherXsrf, HttpMethod.Post, Url, response)).StatusCode);
    }

    [Fact]
    public async Task PersonRemovesOnlyTheirOwnPasskeys()
    {
        var (ann, annXsrf) = await SignInAsync("pk-ann@example.com");
        var (bob, bobXsrf) = await SignInAsync("pk-bob@example.com");
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(ann, annXsrf, authenticator);
        var annsId = (await ann.GetFromJsonAsync<List<PasskeyResponse>>(Url, Ct))!.Last().Id;

        Assert.Equal(HttpStatusCode.NotFound, (await Send(bob, bobXsrf, HttpMethod.Delete, $"{Url}/{annsId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(ann, annXsrf, HttpMethod.Delete, $"{Url}/{annsId}")).StatusCode);
        Assert.DoesNotContain((await ann.GetFromJsonAsync<List<PasskeyResponse>>(Url, Ct))!, p => p.Id == annsId);
    }

    [Fact]
    public async Task AnAccessTokenCannotSaveAPasskey()
    {
        var (client, xsrf) = await SignInAsync("pk-ann@example.com");
        var created = await Send(client, xsrf, HttpMethod.Post, "/api/me/access-tokens", """{"name":"Script","canChange":true}""");
        var secret = (await created.Content.ReadFromJsonAsync<NewAccessTokenResponse>(Ct))!.Secret;
        var bearer = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsync(Url + "/options", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(Url, Ct)).StatusCode);
    }

    [Fact]
    public async Task ErasingPersonalDetailsDeletesThePasskeys()
    {
        var (client, xsrf) = await SignInAsync("pk-gus@example.com");
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(client, xsrf, authenticator);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var gus = await db.People.SingleAsync(p => p.Email == "pk-gus@example.com", Ct);
        await PersonEraser.EraseAsync(db, gus, null);

        Assert.False(await db.Passkeys.AnyAsync(p => p.PersonId == gus.Id, Ct));
    }
}
