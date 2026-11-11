using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// The Company login. The settings API is for Planners, with the login cookie only: it holds the client secret.
// The routes under /login/company start a login. The handler of the provider answers on /signin-oidc.
public static class CompanyLoginEndpoints
{
    public const string SecretAgainDetail = "Enter the client secret again for the new settings.";

    public static void MapCompanyLoginSettings(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings/company-login").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        group.MapGet("/", Get);
        group.MapPut("/", Save);
        group.MapDelete("/", Remove);
    }

    public static void MapCompanyLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CompanyLogin.StartPath, Start);
        app.MapGet(CompanyLogin.TestPath, StartTest);
    }

    // Settings and the first-run screen share these rules. It returns the error text, or null when the values are fine.
    public static string? Validate(CompanyLoginRequest request) =>
        !Enum.IsDefined(request.Kind) ? "Choose the kind of Company login."
        : CompanyIdentity.ValidateIssuer(request.Kind, request.Issuer) is { } issuer ? issuer
        : (request.ClientId?.Trim() ?? "").Length is 0 or > 200 ? "Enter a client ID of 1 to 200 characters."
        : request.ClientSecret is { Length: > 1000 } ? "Enter a client secret of at most 1000 characters."
        : null;

    private static async Task<IResult> Get(HeadroomDbContext db)
    {
        var row = await db.InstanceSettings.AsNoTracking().FirstAsync();
        return Results.Ok(new CompanyLoginResponse(row.CompanyLoginKind, row.CompanyLoginIssuer, row.CompanyLoginClientId, row.CompanyLoginClientSecret is not null));
    }

    // Nothing is stored here. The settings wait in a cookie until the test login of this Planner succeeds.
    private static async Task<IResult> Save(CompanyLoginRequest request, HttpContext http, HeadroomDbContext db, IDataProtectionProvider protection)
    {
        if (Validate(request) is { } invalid) return Problem(400, invalid);
        var (issuer, clientId) = (request.Issuer.Trim(), request.ClientId.Trim());

        // An empty secret keeps the stored one. This holds only for the same kind, issuer and client ID.
        var secret = request.ClientSecret ?? "";
        if (secret.Length == 0)
        {
            var row = await db.InstanceSettings.AsNoTracking().FirstAsync();
            if (row.CompanyLoginClientSecret is null) return Problem(400, "Enter the client secret.");
            if (row.CompanyLoginKind != request.Kind || row.CompanyLoginIssuer != issuer || row.CompanyLoginClientId != clientId)
                return Problem(400, SecretAgainDetail);
            secret = CompanyLogin.ReadSecret(row.CompanyLoginClientSecret, protection)!;
        }
        new CompanyLogin.Pending(request.Kind, issuer, clientId, secret, http.User.PersonId(), null).Write(http);
        return Results.Ok(new CompanyLoginTestResponse(CompanyLogin.TestPath));
    }

    // The identifiers belong to the provider that is removed, so they go too.
    private static async Task<IResult> Remove(HttpContext http, HeadroomDbContext db)
    {
        var row = await db.InstanceSettings.FirstAsync();
        (row.CompanyLoginKind, row.CompanyLoginIssuer, row.CompanyLoginClientId, row.CompanyLoginClientSecret) = (null, null, null, null);
        row.LastChangedBy = http.User.PersonId();
        await db.People.ExecuteUpdateAsync(p => p.SetProperty(x => x.CompanyLoginIssuer, (string?)null).SetProperty(x => x.CompanyLoginSubject, (string?)null));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Start(HttpContext http, HeadroomDbContext db, ILogger<Program> log, string? returnUrl)
    {
        if (!await db.InstanceSettings.AnyAsync(s => s.CompanyLoginKind != null)) return Results.NotFound();
        CompanyLogin.Pending.Delete(http); // a forgotten test login must not change this login
        return await ChallengeAsync(http, log, LoginEndpoints.SafeReturnUrl(returnUrl), "/login");
    }

    private static async Task<IResult> StartTest(HttpContext http, ILogger<Program> log) =>
        CompanyLogin.Pending.Read(http) is { } pending
            ? await ChallengeAsync(http, log, "/", pending.PersonId is not null ? "/settings" : "/first-run")
            : Results.NotFound();

    // The handler loads the metadata of the provider here. A wrong issuer ends in the same outcome as a refusal.
    private static async Task<IResult> ChallengeAsync(HttpContext http, ILogger<Program> log, string returnUrl, string backTo)
    {
        try
        {
            await http.ChallengeAsync(CompanyLogin.Scheme, new AuthenticationProperties { RedirectUri = returnUrl });
            return Results.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("Could not start the Company login: {ExceptionType}", ex.GetType().Name);
            return Results.Redirect($"{backTo}?companyLogin={CompanyLoginOutcome.Provider}");
        }
    }
}
