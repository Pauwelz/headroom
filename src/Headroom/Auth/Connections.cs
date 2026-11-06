using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Headroom.Auth;

// A Connection is an OpenIddict authorization (ADR 0008). Deleting it and its tokens makes the access and refresh tokens fail at once (entry validation).
public static class Connections
{
    // The Connections of one Person, or all of them when personId is null (a Planner).
    public static IQueryable<OpenIddictEntityFrameworkCoreAuthorization> OwnedBy(this IQueryable<OpenIddictEntityFrameworkCoreAuthorization> connections, Guid? personId)
    {
        var subject = personId?.ToString();
        return subject is null ? connections : connections.Where(a => a.Subject == subject);
    }

    public static async Task<int> DeleteAsync(HeadroomDbContext db, IQueryable<OpenIddictEntityFrameworkCoreAuthorization> connections)
    {
        await db.Set<OpenIddictEntityFrameworkCoreToken>().Where(t => connections.Any(a => a.Id == t.Authorization!.Id)).ExecuteDeleteAsync();
        return await connections.ExecuteDeleteAsync();
    }

    public static Task DeleteOfPersonAsync(HeadroomDbContext db, Guid personId) =>
        DeleteAsync(db, db.Set<OpenIddictEntityFrameworkCoreAuthorization>().OwnedBy(personId));
}
