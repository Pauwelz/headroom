using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Access tokens (ADR 0006). Only the login cookie works here: a token cannot manage tokens.
public static class AccessTokenEndpoints
{
    public static void MapAccessTokenEndpoints(this RouteGroupBuilder api)
    {
        var own = api.MapGroup("/me/access-tokens").RequireAuthorization(AccessTokenAuth.NoTokenPolicy);
        own.MapGet("", ListOwn);
        own.MapPost("", AddOwn);
        own.MapDelete("/{id:guid}", RevokeOwn);

        var all = api.MapGroup("/settings/access-tokens").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        all.MapGet("", ListAll);
        all.MapDelete("/{id:guid}", RevokeAny);
    }

    private static async Task<IResult> ListOwn(HttpContext http, HeadroomDbContext db) =>
        Results.Ok(await ListAsync(db, http.User.PersonId()));

    private static async Task<IResult> ListAll(HeadroomDbContext db) => Results.Ok(await ListAsync(db, null));

    private static async Task<List<AccessTokenResponse>> ListAsync(HeadroomDbContext db, Guid? personId) =>
        await (from t in db.AccessTokens.AsNoTracking()
               join p in db.People on t.PersonId equals p.Id
               where personId == null || t.PersonId == personId
               orderby t.DateModified
               select new AccessTokenResponse("AccessToken", t.Id, t.Name, p.Name, t.DateModified, t.ExpiresAt, t.CanChange)).ToListAsync();

    // The secret shows once, in this response. Only its hash is stored.
    private static async Task<IResult> AddOwn(AccessTokenRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        if (ApiProblem.CleanName(request.Name) is not { } name) return ApiProblem.Problem(400, ApiProblem.NameDetail);
        if (!AccessTokenExpiry.TryExpiresAt(request.Expiry, time.GetUtcNow(), out var expiresAt))
            return ApiProblem.Problem(400, $"Choose {AccessTokenExpiry.ChoiceText}.");
        var person = await MeEndpoints.OfCallerAsync(http, db);
        var secret = AccessTokenAuth.NewSecret();
        var token = new AccessToken { Name = name, PersonId = person.Id, TokenHash = AccessTokenAuth.Hash(secret), ExpiresAt = expiresAt, CanChange = request.CanChange, LastChangedBy = person.Id };
        db.AccessTokens.Add(token);
        await db.SaveChangesAsync();
        return Results.Ok(new NewAccessTokenResponse("AccessToken", token.Id, token.Name, person.Name, token.DateModified, token.ExpiresAt, token.CanChange, secret));
    }

    private static async Task<IResult> RevokeOwn(Guid id, HttpContext http, HeadroomDbContext db) =>
        await RevokeAsync(db, id, http.User.PersonId());

    private static async Task<IResult> RevokeAny(Guid id, HeadroomDbContext db) => await RevokeAsync(db, id, null);

    // Revoking deletes the row, so the token gets 401 at once.
    private static async Task<IResult> RevokeAsync(HeadroomDbContext db, Guid id, Guid? personId) =>
        await db.AccessTokens.Where(t => t.Id == id && (personId == null || t.PersonId == personId)).ExecuteDeleteAsync() == 0
            ? Results.NotFound() : Results.NoContent();
}
