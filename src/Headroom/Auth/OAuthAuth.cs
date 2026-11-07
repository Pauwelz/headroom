using System.Security.Claims;
using System.Text.Encodings.Web;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Headroom.Auth;

// Bearer tokens of a Connected app (ADR 0008). BearerScheme sends "hr_" tokens to the Access token scheme and every other
// bearer token to the OAuth scheme below. /mcp and /api both use BearerScheme, and never the login cookie.
public static class OAuthAuth
{
    public const string Scheme = "OAuth";
    public const string BearerScheme = "HeadroomBearer";
    // The scope of a Connection that its Person allowed to change things. Authorize stores it on the Connection.
    public const string ChangeScope = "headroom:change";

    public static string SelectBearerScheme(HttpRequest request) =>
        AccessTokenAuth.HasAccessTokenSecret(request) ? AccessTokenAuth.Scheme : Scheme;

    // The Person of an OAuth subject, or null when the Person is unknown, erased or past their end date.
    // The token ticket (authorize, code exchange) and every call use this one rule, like AccessTokenAuth.Handler.
    public static async Task<Person?> ActivePersonAsync(HeadroomDbContext db, TimeProvider time, string? subject)
    {
        if (!Guid.TryParse(subject, out var id)) return null;
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return person is null || person.ErasedNumber is not null || !person.CanLogIn(BrusselsTime.Today(time)) ? null : person;
    }

    // Validates the OAuth access token, then builds the same kind of principal as an Access token: the Person's id and role,
    // marked as a token (so NoTokenPolicy and the antiforgery bypass apply) and with "Can change" only when the Connection has it.
    internal sealed class Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        HeadroomDbContext db, TimeProvider time, OAuthSetup.Resources resources, IOpenIddictAuthorizationManager connections)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!AccessTokenAuth.HasBearerHeader(Request)) return AuthenticateResult.NoResult();
            var result = await Context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded) return AuthenticateResult.Fail(result.Failure?.Message ?? "Invalid access token.");
            // One audience per endpoint: a token for /mcp does not work on /api, and the reverse.
            var audience = Request.Path.StartsWithSegments(Mcp.McpSetup.Path) ? resources.Mcp : resources.Api;
            if (!result.Principal!.GetAudiences().Contains(audience)) return AuthenticateResult.Fail("This token is for another resource.");
            var person = await ActivePersonAsync(db, time, result.Principal.GetClaim(OpenIddictConstants.Claims.Subject));
            if (person is null) return AuthenticateResult.Fail("The Person of this Connection has left.");
            // "Can change" is read from the Connection on each call, so it ends when the Connection is revoked. Its reach comes from the Person.
            var connection = result.Principal.GetAuthorizationId() is { } id ? await connections.FindByIdAsync(id) : null;
            var canChange = connection is not null && (await connections.GetScopesAsync(connection)).Contains(ChangeScope);
            var claims = new List<Claim> { new(AccessTokenAuth.TokenClaim, result.Principal.GetTokenId() ?? "oauth") };
            if (canChange) claims.Add(new Claim(AccessTokenAuth.CanChangeClaim, "true"));
            var principal = PersonPrincipal.Create(person, OAuthAuth.Scheme, [.. claims]);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, OAuthAuth.Scheme));
        }
    }
}
