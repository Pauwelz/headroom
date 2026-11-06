using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMe).RequireAuthorization();
        // Ends the session on the server: the cookie handler deletes the stored session.
        api.MapPost("/logout", () => Results.SignOut(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]))
            .RequireAuthorization();
    }

    private static async Task<IResult> GetMe(HttpContext http, HeadroomDbContext db)
    {
        var person = await OfCallerAsync(http, db);
        ApiAntiforgery.IssueToken(http);
        return Results.Ok(ToResponse(person));
    }

    public static async Task<Person> OfCallerAsync(HttpContext http, HeadroomDbContext db)
    {
        var id = http.User.PersonId()!.Value; // authentication fails without a Person
        return await db.People.AsNoTracking().FirstAsync(p => p.Id == id);
    }

    public static MeResponse ToResponse(Person person) => new("Person", person.Id, person.Name, person.Email!, person.IsPlanner);
}
