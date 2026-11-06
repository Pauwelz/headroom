using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headroom.Auth;

// The bearer scheme. It works on any endpoint, so /mcp can reuse it. The Person behind the token acts:
// same Person id and Planner role as a login, read again from the Person on each call.
public static class AccessTokenAuth
{
    public const string Scheme = "Bearer";
    public const string Prefix = "hr_";
    private const string BearerPrefix = "Bearer ";
    // Policy for the token endpoints: only the login cookie, never a bearer token.
    public const string NoTokenPolicy = "NoAccessToken";
    internal const string TokenClaim = "headroom:access-token";
    internal const string CanChangeClaim = "headroom:can-change";

    // SHA-256 is enough: the secret is 256 random bits, so there is nothing to brute-force.
    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public static string NewSecret() => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool IsAccessToken(this ClaimsPrincipal user) => user.HasClaim(c => c.Type == TokenClaim);

    // The one "Can change" check. A login always can change. A token can when it has the claim.
    // REST uses it in ApiAntiforgery. The MCP write tools must call it too.
    public static bool CanChange(this ClaimsPrincipal user) => !user.IsAccessToken() || user.HasClaim(c => c.Type == CanChangeClaim);

    public static bool HasBearerHeader(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);

    // "Bearer hr_...": the secret of an Access token. Any other bearer token belongs to a Connected app.
    public static bool HasAccessTokenSecret(HttpRequest request) =>
        HasBearerHeader(request) && request.Headers.Authorization.ToString()[BearerPrefix.Length..].TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    internal sealed class Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, HeadroomDbContext db, TimeProvider time)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!HasBearerHeader(Request)) return AuthenticateResult.NoResult();
            var secret = Request.Headers.Authorization.ToString()[BearerPrefix.Length..].Trim();
            var hash = Hash(secret);
            var token = await db.AccessTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (token is null) return AuthenticateResult.Fail("Unknown or revoked Access token.");
            if (AccessTokenExpiry.IsExpired(token.ExpiresAt, time.GetUtcNow())) return AuthenticateResult.Fail("This Access token has expired.");
            var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == token.PersonId);
            if (person is null || person.ErasedNumber is not null || !person.CanLogIn(BrusselsTime.Today(time)))
                return AuthenticateResult.Fail("The Person of this Access token has left.");
            var claims = new List<Claim> { new(TokenClaim, token.Id.ToString()) };
            // Rights follow the role: a token of a Person who is no longer a Planner cannot change.
            if (token.CanChange && person.IsPlanner) claims.Add(new Claim(CanChangeClaim, "true"));
            var principal = PersonPrincipal.Create(person, AccessTokenAuth.Scheme, [.. claims]);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, AccessTokenAuth.Scheme));
        }
    }
}
