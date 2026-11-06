using System.Net;
using System.Net.Http.Json;
using System.Web;
using System.Text;
using System.Text.Json;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenIddict.Abstractions;

namespace Headroom.Tests.Api;

// A Person connects a Connected app and the app reads /mcp as that Person.
public class OAuthMcpTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Ann = "oauth-ann@example.com";
    private const string Demoted = "oauth-demoted@example.com";
    private const string Old = "oauth-old@example.com";
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly OAuthFlow _flow;

    public OAuthMcpTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_mail)));
        _flow = new OAuthFlow(_factory, _mail);
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann Planner", Email = Ann, IsPlanner = true };
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Zed", Email = "oauth-zed@example.com" });
        db.People.Add(new Person { Name = "Old", Email = Old });
        db.People.Add(new Person { Name = "Demoted", Email = Demoted, IsPlanner = true });
        var client = new Headroom.Data.Client { Name = "Client" };
        db.Clients.Add(client);
        db.Webhooks.Add(new Webhook { Name = "Hook", Url = "https://hooks.example.com/a", Secret = "whsec_x", LastChangedBy = ann.Id });
        db.Projects.Add(new Project { ClientId = client.Id, Name = "Project", Status = ProjectStatus.Confirmed });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    private async Task<McpClient> McpAsync(string token) => await McpClient.CreateAsync(new HttpClientTransport(
        new HttpClientTransportOptions { Endpoint = new Uri(OAuthFlow.Mcp) }, _flow.BearerClient(token), ownsHttpClient: true), McpTests.ClientOptions(), cancellationToken: Ct);

    private async Task<HttpResponseMessage> PostMcpAsync(string token) => await _flow.BearerClient(token).PostAsync("/mcp", Json(), Ct);

    [Fact]
    public async Task McpPublishesProtectedResourceMetadata()
    {
        var response = await _flow.NewClient().GetAsync("/.well-known/oauth-protected-resource/mcp", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal(OAuthFlow.Mcp, json.GetProperty("resource").GetString());
        Assert.Equal("https://localhost/", json.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task TheAuthorizationServerAdvertisesS256Only()
    {
        var response = await _flow.NewClient().GetAsync("/.well-known/oauth-authorization-server", Ct);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal(["S256"], json.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("https://localhost/", json.GetProperty("issuer").GetString());
    }

    // RFC 8414: a client refuses the server when the issuer differs from the authorization_servers entry, even by a trailing slash.
    [Theory]
    [InlineData("mcp")]
    [InlineData("api")]
    public async Task TheIssuerIsTheExactAuthorizationServerOfEachResource(string resource)
    {
        var client = _flow.NewClient();
        var server = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-authorization-server", Ct)).RootElement;
        var metadata = JsonDocument.Parse(await client.GetStringAsync($"/.well-known/oauth-protected-resource/{resource}", Ct)).RootElement;

        Assert.Equal(server.GetProperty("issuer").GetString(), metadata.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task McpWithoutATokenAnswers401WithTheMetadataAddress()
    {
        var response = await _flow.NewClient().PostAsync("/mcp", Json(), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task AConnectedAppReadsMcpAsThePersonWhoConnected()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Ann);

        await using var mcp = await McpAsync(token);
        var me = await mcp.CallToolAsync("get_me", cancellationToken: Ct);

        Assert.Contains("Ann Planner", string.Join(" ", me.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    [Fact]
    public async Task APersonWithoutASessionLogsInAndReturnsToTheConsentScreen()
    {
        var app = await _flow.AddAppAsync();
        var client = _flow.NewClient();

        var toLogin = await client.GetAsync(OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New()), Ct);
        Assert.Equal(HttpStatusCode.Redirect, toLogin.StatusCode);
        var loginUrl = toLogin.Headers.Location!.PathAndQuery;
        Assert.StartsWith("/login?ReturnUrl=", loginUrl);

        var done = await LogInAsync(client, loginUrl, Ann);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        var consent = await client.GetAsync(done.Headers.Location, Ct);

        var html = await consent.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Claude", html);
        Assert.Contains("Ann Planner", html);
    }

    [Fact]
    public async Task LoginDoesNotRedirectToAnotherSite()
    {
        var done = await LogInAsync(_flow.NewClient(), "/login?ReturnUrl=https%3A%2F%2Fevil.example", Ann);

        Assert.Equal("/", done.Headers.Location!.OriginalString);
    }

    private async Task<HttpResponseMessage> LogInAsync(HttpClient client, string loginUrl, string email)
    {
        await PostFormAsync(client, loginUrl, "/login", ("email", email));
        return await PostFormAsync(client, loginUrl, "/login/verify", ("email", email), ("code", _mail.Sent.Last(m => m.To == email).Code));
    }

    // Posts the form of a page: its hidden fields (antiforgery token, return URL) plus the given fields.
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string action, params (string, string)[] fields)
    {
        var html = await client.GetStringAsync(pageUrl, Ct);
        var form = OAuthFlow.Hidden(html).Where(h => fields.All(f => f.Item1 != h.Key)).Concat(fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2)));
        return await client.PostAsync(action, new FormUrlEncodedContent(form), Ct);
    }

    [Fact]
    public async Task ConsentNamesTheConnectedAppAndThePerson()
    {
        var app = await _flow.AddAppAsync("Copilot Studio");
        var client = await TestLogin.SignInAsync(_factory, _mail, "oauth-zed@example.com");

        var html = await (await client.GetAsync(OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New()), Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Copilot Studio", html);
        Assert.Contains("Zed", html);
    }

    [Fact]
    public async Task ADeniedConsentSendsAccessDeniedAndNoCode()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var response = await OAuthFlow.ConsentAsync(client, OAuthFlow.AuthorizeUrl(await _flow.AddAppAsync(), OAuthFlow.Pkce.New()), "deny");

        Assert.Equal("access_denied", OAuthFlow.QueryOf(response, "error"));
        Assert.Null(OAuthFlow.QueryOf(response, "code"));
    }

    [Fact]
    public async Task ApprovingTwiceKeepsOneConnection()
    {
        var app = await _flow.AddAppAsync();
        await _flow.TokenAsync(app, Ann);
        await _flow.TokenAsync(app, Ann);

        using var scope = _factory.Services.CreateScope();
        var personId = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Single(p => p.Email == Ann).Id.ToString();
        var connections = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>()
            .FindByApplicationIdAsync(await _flow.IdOfAsync(app), Ct).ToListAsync(Ct);
        Assert.Single(connections);
        Assert.Equal(personId, await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().GetSubjectAsync(connections[0], Ct));
    }

    [Theory]
    [InlineData("no-pkce")]
    [InlineData("plain")]
    [InlineData("unknown-client")]
    [InlineData("bad-redirect")]
    [InlineData("two-resources")]
    [InlineData("other-resource")]
    public async Task AnInvalidAuthorizeRequestGetsNoCodeAndNoConsentScreen(string problem)
    {
        var app = await _flow.AddAppAsync();
        var pkce = OAuthFlow.Pkce.New();
        var url = problem switch
        {
            "no-pkce" => OAuthFlow.AuthorizeUrl(app, null),
            "plain" => OAuthFlow.AuthorizeUrl(app, pkce, method: "plain"),
            "unknown-client" => OAuthFlow.AuthorizeUrl(new("nobody", "x"), pkce),
            "bad-redirect" => OAuthFlow.AuthorizeUrl(app, pkce, redirect: "https://evil.example/cb"),
            "two-resources" => OAuthFlow.AuthorizeUrl(app, pkce) + "&resource=" + Uri.EscapeDataString(OAuthFlow.Api),
            _ => OAuthFlow.AuthorizeUrl(app, pkce, resource: "https://localhost/other"),
        };
        var client = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var response = await client.GetAsync(url, Ct);

        Assert.False(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync(Ct)).Contains("decision"));
        Assert.Null(response.Headers.Location is null ? null : OAuthFlow.QueryOf(response, "code"));
    }

    [Fact]
    public async Task AWriteThroughMcpWithAnOAuthTokenIsRefused()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Ann);

        await using var mcp = await McpAsync(token);
        var result = await mcp.CallToolAsync("add_time_off",
            new Dictionary<string, object?> { ["personId"] = Guid.NewGuid(), ["startDate"] = "2026-12-01", ["startPart"] = "morning", ["endDate"] = "2026-12-01", ["endPart"] = "afternoon" }, cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Contains("read only", string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    private static readonly Dictionary<string, object?> NewAllocation = new() { ["startDate"] = "2026-12-07", ["endDate"] = "2026-12-11", ["percentage"] = 40 };

    private async Task<Dictionary<string, object?>> AllocationArgsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        return new(NewAllocation) { ["personId"] = (await db.People.SingleAsync(p => p.Email == Ann, Ct)).Id, ["projectId"] = (await db.Projects.SingleAsync(Ct)).Id };
    }

    [Fact]
    public async Task OnlyAPlannerSeesCanChangeOnTheConsentScreen()
    {
        var app = await _flow.AddAppAsync();
        var url = OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New());

        var planner = await (await TestLogin.SignInAsync(_factory, _mail, Ann)).GetStringAsync(url, Ct);
        var other = await (await TestLogin.SignInAsync(_factory, _mail, "oauth-zed@example.com")).GetStringAsync(url, Ct);

        Assert.Contains("Can change", planner);
        Assert.DoesNotContain("Can change", other);
    }

    [Fact]
    public async Task AConnectionWithoutCanChangeCannotCreateAnAllocation()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Ann);

        await using var mcp = await McpAsync(token);
        var result = await mcp.CallToolAsync("create_allocation", await AllocationArgsAsync(), cancellationToken: Ct);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task APlannersConnectionWithCanChangeCreatesAnAllocationAsThatPerson()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Ann, canChange: true);

        await using var mcp = await McpAsync(token);
        var args = await AllocationArgsAsync();
        var result = await mcp.CallToolAsync("create_allocation", args, cancellationToken: Ct);

        Assert.NotEqual(true, result.IsError);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var allocation = await db.Allocations.SingleAsync(a => a.PersonId == (Guid)args["personId"]! && a.StartDate == new DateOnly(2026, 12, 7), Ct);
        Assert.Equal(allocation.PersonId, allocation.LastChangedBy);
        Assert.Contains(await db.Events.Select(e => e.Payload).ToListAsync(Ct), p => p.Contains("headroom.Allocation.created") && p.Contains(allocation.Id.ToString()));
    }

    [Fact]
    public async Task AForgedCanChangeFromANonPlannerIsIgnored()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), "oauth-zed@example.com", canChange: true);

        await using var mcp = await McpAsync(token);
        var result = await mcp.CallToolAsync("create_allocation", await AllocationArgsAsync(), cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Contains("not a Planner", string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    [Fact]
    public async Task ApprovingAgainWithoutCanChangeRemovesIt()
    {
        var app = await _flow.AddAppAsync();
        await _flow.TokenAsync(app, Ann, canChange: true);
        var token = await _flow.TokenAsync(app, Ann);

        await using var mcp = await McpAsync(token);
        Assert.True((await mcp.CallToolAsync("create_allocation", await AllocationArgsAsync(), cancellationToken: Ct)).IsError);
    }

    [Fact]
    public async Task APersonWhoLosesThePlannerRoleCanNoLongerWriteWithTheSameToken()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Demoted, canChange: true);
        var args = await AllocationArgsAsync();
        args["startDate"] = "2026-12-14";
        args["endDate"] = "2026-12-18";
        await using var mcp = await McpAsync(token);
        Assert.NotEqual(true, (await mcp.CallToolAsync("create_allocation", args, cancellationToken: Ct)).IsError);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Where(p => p.Email == Demoted)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, false), Ct);

        Assert.True((await mcp.CallToolAsync("create_allocation", args, cancellationToken: Ct)).IsError);
    }

    [Fact]
    public async Task APersonPastTheirEndDateGets401()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync(), Old);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Where(p => p.Email == Old)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndDate, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3)), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostMcpAsync(token)).StatusCode);
    }

    // Copilot Studio sends no resource parameter. The token is then for /mcp only.
    [Fact]
    public async Task AnAuthorizeRequestWithoutAResourceGetsATokenForMcp()
    {
        var app = await _flow.AddAppAsync();
        var tokens = await _flow.TokensAsync(app, Ann, resource: null);
        var token = tokens.GetProperty("access_token").GetString()!;

        await using (var mcp = await McpAsync(token))
            Assert.Contains("Ann Planner", string.Join(" ", (await mcp.CallToolAsync("get_me", cancellationToken: Ct)).Content.OfType<TextContentBlock>().Select(c => c.Text)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _flow.BearerClient(token).GetAsync("/api/me", Ct)).StatusCode);
        var refreshed = await _flow.RefreshAsync(app, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task ATokenForOneResourceIsRefusedOnTheOther()
    {
        var app = await _flow.AddAppAsync();
        var mcpToken = await _flow.TokenAsync(app, Ann);
        var apiToken = await _flow.TokenAsync(app, Ann, OAuthFlow.Api);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _flow.BearerClient(mcpToken).GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostMcpAsync(apiToken)).StatusCode);
    }

    [Fact]
    public async Task ApiPublishesProtectedResourceMetadata()
    {
        var response = await _flow.NewClient().GetAsync("/.well-known/oauth-protected-resource/api", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal(OAuthFlow.Api, json.GetProperty("resource").GetString());
        Assert.Equal("https://localhost/", json.GetProperty("authorization_servers")[0].GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    public async Task ApiWithoutAValidTokenAnswers401WithTheMetadataAddress(string? bearer)
    {
        var http = bearer is null ? _flow.NewClient() : _flow.BearerClient(bearer);

        var response = await http.GetAsync("/api/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/api\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task AWebPageGetsNoBearerChallenge()
    {
        var response = await _flow.NewClient().GetAsync("/login", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task AnOAuthTokenForApiIsReadOnlyThereUntilCanChangeIsGranted()
    {
        var http = _flow.BearerClient(await _flow.TokenAsync(await _flow.AddAppAsync(), Ann, OAuthFlow.Api));

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/people", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/api/allocations", Json(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.DeleteAsync($"/api/allocations/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task AnOAuthTokenCannotManageAccessTokensOrConnectedApps()
    {
        var http = _flow.BearerClient(await _flow.TokenAsync(await _flow.AddAppAsync(), Ann, OAuthFlow.Api));

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/me/access-tokens", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/settings/connected-apps", Ct)).StatusCode);
    }

    [Fact]
    public async Task ACodeForOneResourceCannotBeExchangedForAnother()
    {
        var app = await _flow.AddAppAsync();
        var pkce = OAuthFlow.Pkce.New();
        var redirect = await _flow.AuthorizeAsync(Ann, OAuthFlow.AuthorizeUrl(app, pkce));

        var response = await _flow.ExchangeAsync(app, OAuthFlow.QueryOf(redirect, "code")!, pkce, OAuthFlow.Api);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_target", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DeletingAConnectedAppRevokesItsTokens()
    {
        var app = await _flow.AddAppAsync("Doomed");
        var token = await _flow.TokenAsync(app, Ann);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await PostMcpAsync(token)).StatusCode);

        var session = await TestLogin.SignInAsync(_factory, _mail, Ann);
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/settings/connected-apps/" + await _flow.IdOfAsync(app));
        delete.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(session));
        Assert.Equal(HttpStatusCode.NoContent, (await session.SendAsync(delete, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostMcpAsync(token)).StatusCode);
    }

    private async Task<HttpResponseMessage> PostAllocationAsync(HttpClient http, string start, string end)
    {
        var args = await AllocationArgsAsync();
        args["startDate"] = start;
        args["endDate"] = end;
        return await http.PostAsJsonAsync("/api/allocations", args, Ct);
    }

    [Fact]
    public async Task AnApiTokenWithCanChangeWritesOnApiAndWithoutItGets403()
    {
        var app = await _flow.AddAppAsync();
        var readOnly = _flow.BearerClient(await _flow.TokenAsync(app, Ann, OAuthFlow.Api));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAllocationAsync(readOnly, "2027-01-04", "2027-01-08")).StatusCode);

        var writer = _flow.BearerClient(await _flow.TokenAsync(await _flow.AddAppAsync(), Ann, OAuthFlow.Api, canChange: true));

        Assert.True((await PostAllocationAsync(writer, "2027-01-04", "2027-01-08")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task ARefreshedAccessTokenKeepsCanChange()
    {
        var app = await _flow.AddAppAsync();
        var tokens = await _flow.TokensAsync(app, Ann, canChange: true);
        var refreshed = await (await _flow.RefreshAsync(app, tokens.GetProperty("refresh_token").GetString()!)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        await using var mcp = await McpAsync(refreshed.GetProperty("access_token").GetString()!);
        var args = await AllocationArgsAsync();
        args["startDate"] = "2027-01-11";
        args["endDate"] = "2027-01-15";

        Assert.NotEqual(true, (await mcp.CallToolAsync("create_allocation", args, cancellationToken: Ct)).IsError);
    }

    [Fact]
    public async Task AnAuthorizeRequestForOpenidIsRefused()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var response = await client.GetAsync(OAuthFlow.AuthorizeUrl(await _flow.AddAppAsync(), OAuthFlow.Pkce.New(), scope: "openid offline_access"), Ct);

        Assert.False(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync(Ct)).Contains("decision"));
        Assert.Null(response.Headers.Location is null ? null : OAuthFlow.QueryOf(response, "code"));
    }

    [Fact]
    public async Task AnAuthorizeRequestWithAChallengeButNoMethodIsRefused()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var response = await client.GetAsync(OAuthFlow.AuthorizeUrl(await _flow.AddAppAsync(), OAuthFlow.Pkce.New(), method: ""), Ct);

        Assert.False(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync(Ct)).Contains("decision"));
        Assert.Null(response.Headers.Location is null ? null : OAuthFlow.QueryOf(response, "code"));
    }

    [Fact]
    public async Task ARefreshForAnotherResourceIsRefused()
    {
        var app = await _flow.AddAppAsync();
        var refresh = (await _flow.TokensAsync(app, Ann)).GetProperty("refresh_token").GetString()!;

        var response = await _flow.RefreshAsync(app, refresh, OAuthFlow.Api);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_target", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DeletingAConnectedAppRevokesItsRefreshToken()
    {
        var app = await _flow.AddAppAsync("Doomed2");
        var refresh = (await _flow.TokensAsync(app, Ann)).GetProperty("refresh_token").GetString()!;
        var session = await TestLogin.SignInAsync(_factory, _mail, Ann);
        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/settings/connected-apps/" + await _flow.IdOfAsync(app));
        delete.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(session));
        Assert.Equal(HttpStatusCode.NoContent, (await session.SendAsync(delete, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _flow.RefreshAsync(app, refresh)).StatusCode); // invalid_client: the app is gone
    }

    [Fact]
    public async Task AConsentDecisionWithoutASessionReturnsToTheAuthorizeRequest()
    {
        var app = await _flow.AddAppAsync();
        var url = OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New());
        var html = await (await TestLogin.SignInAsync(_factory, _mail, Ann)).GetStringAsync(url, Ct);

        var response = await _flow.NewClient().PostAsync("/connect/authorize", new FormUrlEncodedContent(OAuthFlow.Hidden(html).Append(new("decision", "allow"))), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var returnUrl = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["ReturnUrl"];
        Assert.StartsWith("/connect/authorize?", returnUrl);
        Assert.Contains("client_id=" + app.ClientId, returnUrl);
    }
}
