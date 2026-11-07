using System.Security.Cryptography;
using Headroom.Data;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Headroom.Auth;

// The OAuth server of ADR 0008. Applications live in the database (OpenIddict EF Core store). Tokens use the Data Protection key ring
// that AddHeadroomAuth already keeps in the database, so no signing key or certificate exists and tokens survive scale to zero.
public static class OAuthSetup
{
    // The RFC 8707 resources. A token is valid for one of them only.
    public record Resources(string Mcp, string Api, string Instance)
    {
        // The issuer exactly as OpenIddict publishes it (a URI with a trailing slash). The resource metadata must name the same string.
        public string Issuer => new Uri(Instance).AbsoluteUri;
    }

    // Ephemeral keys, made once per process. AddEphemeral*Key makes new RSA keys for each host, which made every test host slow.
    private static readonly RsaSecurityKey EphemeralEncryptionKey = new(RSA.Create(2048));
    private static readonly RsaSecurityKey EphemeralSigningKey = new(RSA.Create(2048));

    public static void AddHeadroomOAuth(this IServiceCollection services, IConfiguration config)
    {
        // shortcut: the default is only for tests and local runs, a deployment always sets BaseUri.
        var baseUri = (config["BaseUri"] ?? "https://localhost").TrimEnd('/');
        var resources = new Resources(baseUri + Mcp.McpSetup.Path, baseUri + "/api", baseUri);
        services.AddSingleton(resources);
        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<HeadroomDbContext>())
            .AddServer(o =>
            {
                o.SetIssuer(new Uri(resources.Issuer));
                o.SetAuthorizationEndpointUris("connect/authorize").SetTokenEndpointUris("connect/token");
                o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                o.Configure(options => options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));
                o.SetAccessTokenLifetime(TimeSpan.FromHours(1)).SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                // The default leeway of 30 s lets an old refresh token work again; reuse must fail at once.
                o.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                o.RegisterScopes(OpenIddictConstants.Scopes.OfflineAccess);
                o.RegisterResources(resources.Mcp, resources.Api);
                // OpenIddict refuses to start without these keys. They sign and encrypt nothing we issue: the code, access and refresh
                // tokens all use Data Protection. shortcut: they change at each start, so never issue an id token (scope openid).
                o.AddEncryptionKey(EphemeralEncryptionKey).AddSigningKey(EphemeralSigningKey);
                o.UseDataProtection();
                o.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            })
            // The validation reads the token of /mcp and /api with the same key ring. Entry validation looks up the Connection and the
            // token on each call, so a revoked Connection stops working at once.
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseDataProtection();
                o.UseAspNetCore();
                o.EnableAuthorizationEntryValidation().EnableTokenEntryValidation();
            });
    }

    // /mcp publishes its metadata through the MCP scheme. /api has no such scheme, so it gets the RFC 9728 document and
    // challenge here. The challenge goes on every 401 of /api (no token, bad token, expired cookie); the cookie login of the web app is untouched.
    // Runs before UseAuthentication: the MCP scheme answers every path under /.well-known/oauth-protected-resource with the /mcp document.
    public static void UseApiResourceMetadata(this IApplicationBuilder app) =>
        app.Map("/.well-known/oauth-protected-resource/api", branch => branch.Run(http =>
        {
            var r = http.RequestServices.GetRequiredService<Resources>();
            return http.Response.WriteAsJsonAsync(new Dictionary<string, object> { ["resource"] = r.Api, ["authorization_servers"] = new[] { r.Issuer } });
        }));

    public static void UseApiChallenge(this IApplicationBuilder app) =>
        app.UseWhen(http => http.Request.Path.StartsWithSegments("/api"), branch => branch.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                if (http.Response.StatusCode == StatusCodes.Status401Unauthorized && !http.Response.Headers.ContainsKey("WWW-Authenticate"))
                    http.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{http.RequestServices.GetRequiredService<Resources>().Instance}/.well-known/oauth-protected-resource/api\"";
                return Task.CompletedTask;
            });
            return next();
        }));
}
