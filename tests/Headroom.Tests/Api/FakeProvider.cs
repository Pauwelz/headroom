using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Headroom.Tests.Api;

// An OpenID Connect provider in the test process. The handler gets its configuration and signing key directly,
// and its backchannel answers the token endpoint. Each login chooses its own claims.
public sealed class FakeProvider : HttpMessageHandler, IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly SigningCredentials _credentials;
    private string? _idToken;

    public FakeProvider() => _credentials = new(new RsaSecurityKey(_rsa) { KeyId = "test" }, SecurityAlgorithms.RsaSha256);

    // When set, the token endpoint refuses the code like a provider that rejects the client.
    public bool RefuseCode { get; set; }

    // The form of the last token request: it shows the PKCE verifier and the client secret.
    public Dictionary<string, string> LastTokenRequest { get; private set; } = [];

    // Called for the options of the Company login scheme, after all other setup.
    public void Configure(OpenIdConnectOptions options)
    {
        var issuer = options.Authority!;
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = issuer, AuthorizationEndpoint = issuer.TrimEnd('/') + "/authorize", TokenEndpoint = issuer.TrimEnd('/') + "/token",
        };
        configuration.SigningKeys.Add(_credentials.Key);
        options.Configuration = configuration;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        options.Backchannel = new HttpClient(this);
    }

    // Starts at startUrl, answers the authorization request with an ID token with these claims, and returns the callback response.
    public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string startUrl, params (string Name, string Value)[] claims)
    {
        var start = await client.GetAsync(startUrl, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var location = start.Headers.Location!;
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("https://localhost/signin-oidc", query["redirect_uri"]);

        var issuer = location.GetLeftPart(UriPartial.Path)[..^"/authorize".Length];
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = query["client_id"], Expires = DateTime.UtcNow.AddMinutes(5), SigningCredentials = _credentials,
            Claims = claims.ToDictionary(c => c.Name, c => (object)c.Value).Append(KeyValuePair.Create("nonce", (object)query["nonce"]!)).ToDictionary(),
        };
        _idToken = new JsonWebTokenHandler().CreateToken(descriptor);
        return await client.GetAsync($"/signin-oidc?code=abc&state={Uri.EscapeDataString(query["state"]!)}", TestContext.Current.CancellationToken);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
        LastTokenRequest = form.AllKeys.ToDictionary(k => k!, k => form[k]!);
        return RefuseCode
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"id_token":"{{_idToken}}","access_token":"at","token_type":"Bearer","expires_in":3600}""", Encoding.UTF8, "application/json"),
            };
    }

    public new void Dispose()
    {
        _rsa.Dispose();
        base.Dispose();
    }
}
