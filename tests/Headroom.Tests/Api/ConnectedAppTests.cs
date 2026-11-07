using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Headroom.Tests.Api;

public class ConnectedAppTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/connected-apps";
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string ResourcePrefix = OpenIddictConstants.Permissions.Prefixes.Resource;
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ConnectedAppTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IEmailSender>(_mail)));
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann", Email = "app-ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Flo", Email = "app-flo@example.com", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, string Xsrf)> SignInAsync(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string xsrf, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(AntiforgeryToken.HeaderName, xsrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request, Ct);
    }

    private static async Task<CreatedConnectedAppResponse> AddAsync(HttpClient client, string xsrf, string name = "Claude", params string[] redirects)
    {
        var response = await Send(client, xsrf, HttpMethod.Post, Url, new ConnectedAppRequest(name, redirects.Length == 0 ? [Callback] : [.. redirects]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedConnectedAppResponse>(cancellationToken: Ct))!;
    }

    // One application holds the RFC 8707 resources of /mcp and /api.
    [Fact]
    public async Task OneConnectedAppIsAllowedBothResources()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf);

        using var scope = _factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = (await apps.FindByClientIdAsync(created.ClientId, Ct))!;
        Assert.True(await apps.HasPermissionAsync(app, ResourcePrefix + "https://localhost/mcp", Ct));
        Assert.True(await apps.HasPermissionAsync(app, ResourcePrefix + "https://localhost/api", Ct));
        Assert.False(await apps.HasPermissionAsync(app, ResourcePrefix + "https://localhost/other", Ct));
    }

    [Fact]
    public async Task PlannerAddsAConnectedAppAndSeesTheSecretOnce()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf, " Copilot ", Callback, "https://example.com/two");

        Assert.Equal("Copilot", created.Name);
        Assert.False(string.IsNullOrWhiteSpace(created.ClientId));
        Assert.False(string.IsNullOrWhiteSpace(created.ClientSecret));

        var listed = (await ann.GetFromJsonAsync<List<ConnectedAppResponse>>(Url, Ct))!.Single(a => a.Id == created.Id);
        Assert.Equal(created.ClientId, listed.ClientId);
        Assert.Equal([Callback, "https://example.com/two"], listed.RedirectUrls);
        Assert.DoesNotContain(created.ClientSecret, await (await ann.GetAsync(Url, Ct)).Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("http://example.com/cb")]
    [InlineData("/relative")]
    [InlineData("https://example.com/cb#fragment")]
    [InlineData("not a url")]
    public async Task RedirectUrlMustBeAnAbsoluteHttpsUrl(string redirect)
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");

        var response = await Send(ann, xsrf, HttpMethod.Post, Url, new ConnectedAppRequest("Bad", [redirect]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("https", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ConnectedAppNeedsANameAndARedirectUrl()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ann, xsrf, HttpMethod.Post, Url, new ConnectedAppRequest(" ", [Callback]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ann, xsrf, HttpMethod.Post, Url, new ConnectedAppRequest("Claude", []))).StatusCode);
    }

    [Fact]
    public async Task NonPlannerCannotSeeOrChangeConnectedApps()
    {
        var (flo, xsrf) = await SignInAsync("app-flo@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await flo.GetAsync(Url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(flo, xsrf, HttpMethod.Post, Url, new ConnectedAppRequest("Claude", [Callback]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(flo, xsrf, HttpMethod.Put, $"{Url}/x", new ConnectedAppRequest("Claude", [Callback]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(flo, xsrf, HttpMethod.Post, $"{Url}/x/secret")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(flo, xsrf, HttpMethod.Delete, $"{Url}/x")).StatusCode);
    }

    [Fact]
    public async Task AccessTokenCannotManageConnectedApps()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf);
        var bearer = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await NewAccessTokenAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(Url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsJsonAsync(Url, new ConnectedAppRequest("x", [Callback]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PutAsJsonAsync($"{Url}/{created.Id}", new ConnectedAppRequest("x", [Callback]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsync($"{Url}/{created.Id}/secret", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync($"{Url}/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task NewSecretReplacesTheOldOne()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf);
        using var scope = _factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = (await apps.FindByClientIdAsync(created.ClientId, Ct))!;
        Assert.True(await apps.ValidateClientSecretAsync(app, created.ClientSecret, Ct));

        var response = await Send(ann, xsrf, HttpMethod.Post, $"{Url}/{created.Id}/secret");
        var renewed = (await response.Content.ReadFromJsonAsync<CreatedConnectedAppResponse>(cancellationToken: Ct))!;

        using var fresh = _factory.Services.CreateScope(); // the first scope has the old application cached
        apps = fresh.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        app = (await apps.FindByClientIdAsync(created.ClientId, Ct))!;
        Assert.NotEqual(created.ClientSecret, renewed.ClientSecret);
        Assert.Equal(created.ClientId, renewed.ClientId);
        Assert.False(await apps.ValidateClientSecretAsync(app, created.ClientSecret, Ct));
        Assert.True(await apps.ValidateClientSecretAsync(app, renewed.ClientSecret, Ct));
    }

    // Copilot Studio shows its callback URL only after it has the client ID, so the redirect URLs must change afterwards.
    [Fact]
    public async Task PlannerChangesTheRedirectUrlsAndTheClientKeepsItsIdAndSecret()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf, "Copilot", "https://example.com/placeholder");
        const string copilot = "https://global.consent.azure-apim.net/redirect/headroom";

        var response = await Send(ann, xsrf, HttpMethod.Put, $"{Url}/{created.Id}", new ConnectedAppRequest(" Copilot Studio ", [copilot]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await ann.GetFromJsonAsync<List<ConnectedAppResponse>>(Url, Ct))!.Single(a => a.Id == created.Id);
        Assert.Equal("Copilot Studio", listed.Name);
        Assert.Equal(created.ClientId, listed.ClientId);
        Assert.Equal([copilot], listed.RedirectUrls);
        using (var scope = _factory.Services.CreateScope())
        {
            var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Assert.True(await apps.ValidateClientSecretAsync((await apps.FindByClientIdAsync(created.ClientId, Ct))!, created.ClientSecret, Ct));
        }

        var flow = new OAuthFlow(_factory, _mail);
        var app = new OAuthFlow.App(created.ClientId, created.ClientSecret);
        var allowed = await flow.AuthorizeAsync("app-ann@example.com", OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New(), redirect: copilot));
        Assert.StartsWith(copilot, allowed.Headers.Location!.OriginalString);
        Assert.NotNull(OAuthFlow.QueryOf(allowed, "code"));
        var refused = await ann.GetAsync(OAuthFlow.AuthorizeUrl(app, OAuthFlow.Pkce.New(), redirect: "https://example.com/placeholder"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("redirect_uri", await refused.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task AChangeNeedsANameAndValidRedirectUrlsOfAnExistingApp()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ann, xsrf, HttpMethod.Put, $"{Url}/{created.Id}", new ConnectedAppRequest(" ", [Callback]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ann, xsrf, HttpMethod.Put, $"{Url}/{created.Id}", new ConnectedAppRequest("Claude", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ann, xsrf, HttpMethod.Put, $"{Url}/{created.Id}", new ConnectedAppRequest("Claude", ["http://example.com/cb"]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ann, xsrf, HttpMethod.Put, $"{Url}/nobody", new ConnectedAppRequest("Claude", [Callback]))).StatusCode);
        Assert.Equal([Callback], (await ann.GetFromJsonAsync<List<ConnectedAppResponse>>(Url, Ct))!.Single(a => a.Id == created.Id).RedirectUrls);
    }

    [Fact]
    public async Task PlannerDeletesAConnectedApp()
    {
        var (ann, xsrf) = await SignInAsync("app-ann@example.com");
        var created = await AddAsync(ann, xsrf);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(ann, xsrf, HttpMethod.Delete, $"{Url}/{created.Id}")).StatusCode);

        Assert.DoesNotContain((await ann.GetFromJsonAsync<List<ConnectedAppResponse>>(Url, Ct))!, a => a.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ann, xsrf, HttpMethod.Delete, $"{Url}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ann, xsrf, HttpMethod.Post, $"{Url}/{created.Id}/secret")).StatusCode);
    }

    private async Task<string> NewAccessTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var ann = db.People.Single(p => p.Email == "app-ann@example.com");
        var secret = AccessTokenAuth.NewSecret();
        db.AccessTokens.Add(new AccessToken { PersonId = ann.Id, Name = "t", TokenHash = AccessTokenAuth.Hash(secret), CanChange = true, LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
        return secret;
    }
}
