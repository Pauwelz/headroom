using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Set, change and remove the end date of a Person. Planners only. The rules are in EndDateWrites.
public static class EndDateEndpoints
{
    public static void MapEndDateEndpoints(this RouteGroupBuilder people)
    {
        people.MapGet("/{id:guid}/end-date-impact", Impact);
        people.MapPut("/{id:guid}/end-date", Set);
        people.MapDelete("/{id:guid}/end-date", Remove);
    }

    private static async Task<IResult> Impact(Guid id, DateOnly endDate, HeadroomDbContext db, TimeProvider time)
    {
        var (impact, refusal) = await EndDateWrites.ImpactAsync(db, id, endDate, BrusselsTime.Today(time));
        return refusal is null ? Results.Ok(impact) : Refused(refusal);
    }

    private static async Task<IResult> Set(
        Guid id, SetEndDateRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time) =>
        await Saved(await EndDateWrites.SetAsync(db, http.User.PersonId()!.Value, id, request.EndDate, BrusselsTime.Today(time)), db, time);

    private static async Task<IResult> Remove(Guid id, HttpContext http, HeadroomDbContext db, TimeProvider time) =>
        await Saved(await EndDateWrites.RemoveAsync(db, http.User.PersonId()!.Value, id, BrusselsTime.Today(time)), db, time);

    private static async Task<IResult> Saved((Person? Person, Refusal? Refusal) result, HeadroomDbContext db, TimeProvider time) =>
        result.Refusal is { } refusal ? Refused(refusal) : Results.Ok(await PeopleEndpoints.ToResponseAsync(db, result.Person!, BrusselsTime.Today(time)));
}
