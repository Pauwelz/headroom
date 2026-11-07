using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Save, list and remove the Passkeys of the signed-in Person (ADR 0015). Only the login cookie works here:
// an Access token or a Connection cannot save a Passkey.
public static class PasskeyEndpoints
{
    private const string Path = "/api/me/passkeys";

    public static void MapPasskeyEndpoints(this RouteGroupBuilder api)
    {
        var own = api.MapGroup("/me/passkeys").RequireAuthorization(AccessTokenAuth.NoTokenPolicy);
        own.MapGet("", List);
        own.MapPost("/options", Options);
        own.MapPost("", Save);
        own.MapDelete("/{id:guid}", Remove);
    }

    private static async Task<IResult> List(HttpContext http, HeadroomDbContext db) =>
        Results.Ok(await db.Passkeys.AsNoTracking().Where(p => p.PersonId == http.User.PersonId()).OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyResponse("Passkey", p.Id, p.Name, p.CreatedAt, p.LastUsedAt)).ToListAsync());

    private static async Task<IResult> Options(HttpContext http, HeadroomDbContext db, PasskeyCeremony ceremony)
    {
        var person = await MeEndpoints.OfCallerAsync(http, db);
        var existing = await db.Passkeys.AsNoTracking().Where(p => p.PersonId == person.Id).ToListAsync();
        return Results.Content(ceremony.BeginRegistration(http, person, existing, Path), "application/json");
    }

    // The body is the credential.toJSON() of the browser, as plain JSON text.
    private static async Task<IResult> Save(HttpContext http, HeadroomDbContext db, PasskeyCeremony ceremony, TimeProvider time)
    {
        var person = await MeEndpoints.OfCallerAsync(http, db);
        using var reader = new StreamReader(http.Request.Body);
        var credential = await ceremony.CompleteRegistrationAsync(http, person, await reader.ReadToEndAsync(), Path, db);
        if (credential is null) return ApiProblem.Problem(400, "Headroom could not save this passkey. Try again.");
        db.Passkeys.Add(new Passkey
        {
            PersonId = person.Id,
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            UserHandle = PasskeyCeremony.UserHandle(person),
            Transports = credential.Transports.Select(t => t.ToString()).ToArray(),
            Name = PasskeyName.From(http.Request.Headers.UserAgent),
            CreatedAt = time.GetUtcNow(),
            LastChangedBy = person.Id,
        });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Remove(Guid id, HttpContext http, HeadroomDbContext db) =>
        await db.Passkeys.Where(p => p.Id == id && p.PersonId == http.User.PersonId()).ExecuteDeleteAsync() == 0
            ? Results.NotFound()
            : Results.NoContent();
}
