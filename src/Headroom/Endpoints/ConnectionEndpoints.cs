using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Headroom.Endpoints;

// Connections (ADR 0008), like Access tokens: a Person sees and revokes their own, a Planner all of them. Only the login cookie works here.
public static class ConnectionEndpoints
{
    public static void MapConnectionEndpoints(this RouteGroupBuilder api)
    {
        var own = api.MapGroup("/me/connections").RequireAuthorization(AccessTokenAuth.NoTokenPolicy);
        own.MapGet("", (HttpContext http, HeadroomDbContext db) => ListAsync(db, http.User.PersonId()));
        own.MapDelete("/{id}", (string id, HttpContext http, HeadroomDbContext db) => RevokeAsync(db, id, http.User.PersonId()));

        var all = api.MapGroup("/settings/connections").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        all.MapGet("", (HeadroomDbContext db) => ListAsync(db, null));
        all.MapDelete("/{id}", (string id, HeadroomDbContext db) => RevokeAsync(db, id, null));
    }

    // Last used is the newest token of the Connection: a refresh makes a new token.
    private static async Task<IResult> ListAsync(HeadroomDbContext db, Guid? personId)
    {
        var rows = await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().AsNoTracking().OwnedBy(personId)
            .Where(a => a.Status == Statuses.Valid)
            .Select(a => new
            {
                a.Id, App = a.Application!.DisplayName, a.Subject, a.CreationDate, a.Scopes,
                LastUsed = db.Set<OpenIddictEntityFrameworkCoreToken>().Where(t => t.Authorization!.Id == a.Id).Max(t => t.CreationDate),
            }).ToListAsync();
        var owners = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id.ToString());
        return Results.Ok(rows.OrderBy(r => r.CreationDate).Select(r =>
        {
            var owner = owners.GetValueOrDefault(r.Subject ?? "");
            // Scopes is a JSON array; a scope never holds a quote, so a quoted match is exact.
            var canChange = owner is not null && OAuthAuth.CanChange(owner, r.Scopes?.Contains($"\"{OAuthAuth.ChangeScope}\"") == true);
            return new ConnectionResponse(r.Id!, r.App!, owner?.DisplayName ?? "", Utc(r.CreationDate), r.LastUsed is { } used ? Utc(used) : null, canChange);
        }));
    }

    private static DateTimeOffset Utc(DateTime? time) => new(DateTime.SpecifyKind(time ?? default, DateTimeKind.Utc));

    // personId null means any Connection (a Planner).
    private static async Task<IResult> RevokeAsync(HeadroomDbContext db, string id, Guid? personId)
    {
        var mine = db.Set<OpenIddictEntityFrameworkCoreAuthorization>().OwnedBy(personId).Where(a => a.Id == id);
        return await Connections.DeleteAsync(db, mine) == 0
            ? Results.NotFound() : Results.NoContent();
    }
}
