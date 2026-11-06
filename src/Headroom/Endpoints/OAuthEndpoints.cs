using Microsoft.AspNetCore;
using System.Collections.Immutable;
using System.Security.Claims;
using Headroom.Auth;
using Headroom.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Headroom.Endpoints;

// The authorize and token steps of the OAuth server (ADR 0008). OpenIddict validates the request first (client, redirect URL,
// PKCE, known resource) and passes it to these handlers. They add the Headroom rules.
public static class OAuthEndpoints
{
    private const string Server = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], Authorize);
        // The client posts a form without antiforgery token. OpenIddict reads the form, so the handler does not bind it.
        app.MapPost("/connect/token", Exchange).DisableAntiforgery();
    }

    // GET shows the consent screen (after a login when there is no session). POST with the decision signs the code in or refuses.
    private static async Task<IResult> Authorize(
        HttpContext http, IAntiforgery antiforgery, HeadroomDbContext db, TimeProvider time, IOpenIddictApplicationManager apps,
        IOpenIddictAuthorizationManager authorizations, OAuthSetup.Resources known)
    {
        if (http.GetOpenIddictServerRequest() is not { } request) return Results.BadRequest();
        var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded)
            return Results.Challenge(new AuthenticationProperties { RedirectUri = await AuthorizeUrlAsync(http) }, [CookieAuthenticationDefaults.AuthenticationScheme]);
        var person = await OAuthAuth.ActivePersonAsync(db, time, session.Principal!.PersonId()?.ToString());
        if (person is null) return Refuse(Errors.AccessDenied, "The Person cannot connect an app.");
        // The audience of the token is this one resource. Several resources would make the token work on both endpoints.
        // No id token can be issued: the ephemeral keys of OAuthSetup must never sign anything.
        if (request.HasScope(Scopes.OpenId)) return Refuse(Errors.InvalidScope, "The scope openid is not supported.");
        // Copilot Studio sends no resource, so no resource means /mcp.
        var resources = request.GetResources() is { Length: > 0 } sent ? sent : [known.Mcp];
        if (resources.Length != 1) return Refuse(Errors.InvalidTarget, "Send exactly one resource.");

        var app = (await apps.FindByClientIdAsync(request.ClientId!))!;
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            var token = FirstRunEndpoints.Token(http, antiforgery);
            var company = (await db.InstanceSettings.Select(s => s.CompanyName).FirstAsync())!;
            var parameters = http.Request.Query.Select(q => KeyValuePair.Create(q.Key, (string?)q.Value.ToString()));
            return FirstRunEndpoints.Page(ConsentPages.Consent(token, company, (await apps.GetDisplayNameAsync(app))!, person.DisplayName, person.IsPlanner, parameters));
        }

        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(); }
        var form = await http.Request.ReadFormAsync();
        if (form["decision"] != "allow") return Refuse(Errors.AccessDenied, "The Person did not allow the connection.");

        var subject = person.Id.ToString();
        var appId = (await apps.GetIdAsync(app))!;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Server));
        principal.SetClaim(Claims.Subject, subject);
        // Only a Planner can allow "Can change". The form value of anyone else is ignored.
        var canChange = OAuthAuth.CanChange(person, form.ContainsKey("canChange"));
        ImmutableArray<string> scopes = canChange ? [.. request.GetScopes(), OAuthAuth.ChangeScope] : [.. request.GetScopes()];
        principal.SetScopes(scopes);
        principal.SetResources(resources);
        // One Connection per Person and Connected app: approving again reuses it, with the scopes of this approval.
        var connection = await authorizations.FindAsync(subject, appId, Statuses.Valid, AuthorizationTypes.Permanent, []).FirstOrDefaultAsync();
        if (connection is null) connection = await authorizations.CreateAsync(principal, subject, appId, AuthorizationTypes.Permanent, scopes);
        else
        {
            var descriptor = new OpenIddictAuthorizationDescriptor();
            await authorizations.PopulateAsync(descriptor, connection);
            descriptor.Scopes.Clear();
            descriptor.Scopes.UnionWith(scopes);
            await authorizations.UpdateAsync(connection, descriptor);
        }
        principal.SetAuthorizationId(await authorizations.GetIdAsync(connection));
        principal.SetDestinations(_ => [Destinations.AccessToken]);
        return Results.SignIn(principal, properties: null, Server);
    }

    // The code or refresh token carries the Person and the one resource it was approved for. It is signed in again, after the
    // checks that OpenIddict cannot make: the resource must not change, and the Person must still be active.
    private static async Task<IResult> Exchange(HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        if (http.GetOpenIddictServerRequest() is not { } request || !(request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType()))
            return Refuse(Errors.UnsupportedGrantType, "Use the authorization code or the refresh token grant.");
        var principal = (await http.AuthenticateAsync(Server)).Principal!;
        if (!request.GetResources().All(principal.GetResources().Contains))
            return Refuse(Errors.InvalidTarget, "The resource is not the one that was approved.");
        if (await OAuthAuth.ActivePersonAsync(db, time, principal.GetClaim(Claims.Subject)) is null)
            return Refuse(Errors.InvalidGrant, "The Person of this Connection has left.");
        return Results.SignIn(principal, properties: null, Server);
    }

    // The authorize GET with the OAuth parameters of this request. A POST (the consent decision) has them in its form, with the
    // antiforgery token and the decision, which the GET must not carry.
    private static async Task<string> AuthorizeUrlAsync(HttpContext http)
    {
        if (!HttpMethods.IsPost(http.Request.Method)) return http.Request.Path + http.Request.QueryString;
        var form = await http.Request.ReadFormAsync();
        string[] own = ["__RequestVerificationToken", "decision", "canChange"];
        return http.Request.Path + QueryString.Create(form.Where(f => !own.Contains(f.Key)).SelectMany(f => f.Value.Select(v => KeyValuePair.Create(f.Key, v))));
    }

    private static IResult Refuse(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }), [Server]);
}
