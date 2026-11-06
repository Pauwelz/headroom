using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Headroom.Tests.Api;

// Runs the OAuth flow of a Connected app against the real app: authorize, consent, code, token. Later tickets reuse it.
// The factory must use the FakeEmailSender passed here (see TestLogin).
public sealed class OAuthFlow(WebApplicationFactory<Program> factory, FakeEmailSender mail)
{
    public const string Callback = "https://claude.ai/api/mcp/auth_callback";
    public const string Mcp = "https://localhost/mcp";
    public const string Api = "https://localhost/api";

    public sealed record App(string ClientId, string Secret);

    public sealed record Pkce(string Verifier, string Challenge)
    {
        public static Pkce New()
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            return new(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // A Connected app, added straight to the OpenIddict store (the Settings API is tested in ConnectedAppTests).
    public async Task<App> AddAppAsync(string name = "Claude")
    {
        using var scope = factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var secret = Guid.NewGuid().ToString("N");
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = Guid.NewGuid().ToString("N"), ClientSecret = secret, DisplayName = name,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            RedirectUris = { new Uri(Callback) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization, OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code, OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
            },
        };
        descriptor.AddResourcePermissions(Mcp, Api);
        await apps.CreateAsync(descriptor);
        return new(descriptor.ClientId, secret);
    }

    public async Task<string> IdOfAsync(App app)
    {
        using var scope = factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return (await apps.GetIdAsync((await apps.FindByClientIdAsync(app.ClientId))!))!;
    }

    public HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    // A null resource leaves the parameter out, as Copilot Studio does.
    public static string AuthorizeUrl(App app, Pkce? pkce, string? resource = Mcp, string method = "S256", string redirect = Callback, string scope = "offline_access") =>
        $"/connect/authorize?response_type=code&state=st8&scope={Uri.EscapeDataString(scope)}"
        + $"&client_id={app.ClientId}&redirect_uri={Uri.EscapeDataString(redirect)}"
        + (resource is null ? "" : $"&resource={Uri.EscapeDataString(resource)}")
        + (pkce is null ? "" : $"&code_challenge={pkce.Challenge}" + (method == "" ? "" : $"&code_challenge_method={method}"));

    // Signs the Person in, opens the authorize URL and approves the consent screen. Returns the final response (the redirect to the app).
    public async Task<HttpResponseMessage> AuthorizeAsync(string email, string url, bool canChange = false)
    {
        var client = await TestLogin.SignInAsync(factory, mail, email);
        return await ConsentAsync(client, url, canChange: canChange);
    }

    public static async Task<HttpResponseMessage> ConsentAsync(HttpClient signedIn, string url, string decision = "allow", bool canChange = false)
    {
        var page = await signedIn.GetAsync(url);
        if (page.StatusCode != HttpStatusCode.OK) return page;
        var html = await page.Content.ReadAsStringAsync();
        return await signedIn.PostAsync("/connect/authorize", new FormUrlEncodedContent(Hidden(html).Append(new("decision", decision)).Concat(canChange ? [new("canChange", "on")] : [])));
    }

    public static List<KeyValuePair<string, string>> Hidden(string html) =>
        [.. Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">")
            .Select(m => KeyValuePair.Create(m.Groups[1].Value, WebUtility.HtmlDecode(m.Groups[2].Value)))];

    public static string? QueryOf(HttpResponseMessage redirect, string name) =>
        HttpUtility.ParseQueryString(redirect.Headers.Location!.Query)[name];

    public async Task<HttpResponseMessage> ExchangeAsync(App app, string code, Pkce pkce, string? resource = null)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", Callback),
            new("client_id", app.ClientId), new("client_secret", app.Secret), new("code_verifier", pkce.Verifier),
        };
        if (resource is not null) form.Add(new("resource", resource));
        return await NewClient().PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    // The whole flow for a seeded Person. Returns the token response (access_token, refresh_token).
    public async Task<JsonElement> TokensAsync(App app, string email, string? resource = Mcp, bool canChange = false)
    {
        var pkce = Pkce.New();
        var redirect = await AuthorizeAsync(email, AuthorizeUrl(app, pkce, resource), canChange);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var response = await ExchangeAsync(app, QueryOf(redirect, "code")!, pkce);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Returns the access token.
    public async Task<string> TokenAsync(App app, string email, string? resource = Mcp, bool canChange = false) =>
        (await TokensAsync(app, email, resource, canChange)).GetProperty("access_token").GetString()!;

    public async Task<HttpResponseMessage> RefreshAsync(App app, string refreshToken, string? resource = null) => await NewClient().PostAsync("/connect/token",
        new FormUrlEncodedContent([new("grant_type", "refresh_token"), new("refresh_token", refreshToken), new("client_id", app.ClientId), new("client_secret", app.Secret),
            .. resource is null ? Array.Empty<KeyValuePair<string, string>>() : [new("resource", resource)]]));

    public HttpClient BearerClient(string token)
    {
        var http = NewClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
