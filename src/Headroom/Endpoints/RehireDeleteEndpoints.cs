using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Events;
using Headroom.People;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;
using static Headroom.Endpoints.PeopleEndpoints;
using static Headroom.People.PeopleWrites;

namespace Headroom.Endpoints;

// Rehire and delete a Person. Planners only.
public static class RehireDeleteEndpoints
{
    public static void MapRehireAndDeleteEndpoints(this RouteGroupBuilder people)
    {
        people.MapPost("/{id:guid}/rehire", Rehire);
        people.MapGet("/{id:guid}/delete-check", Check);
        people.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> Rehire(
        Guid id, RehireRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        var today = BrusselsTime.Today(time);
        if (PersonLockRules.Rehire(person, today) is { } reason) return Problem(409, reason);
        var end = person.EndDate!.Value;
        if (request.ReturnDate <= end) return Problem(400, $"The return date must be after the end date {DisplayDate.Format(end)}.");
        if (request.Hours is not { IsValid: true }) return Problem(400, "Hours are 0 to 24 per day, in steps of 0.25 hour.");

        var by = http.User.PersonId();
        var gapStart = end.AddDays(1);
        // Capacity is 0 in the gap between the old end date and the return date.
        if (request.ReturnDate > gapStart) db.Contracts.Add(NewContract(id, gapStart, new WeekHours(0, 0, 0, 0, 0, 0, 0), by));
        db.Contracts.Add(NewContract(id, request.ReturnDate, request.Hours, by));
        person.EndDate = null;
        person.LastChangedBy = by;
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(db, person, today));
    }

    private static async Task<IResult> Check(Guid id, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null) return Results.NotFound();
        return Results.Ok(new DeleteCheck(
            await db.Contracts.CountAsync(c => c.PersonId == id), await db.Allocations.CountAsync(a => a.PersonId == id),
            await db.TimeOff.CountAsync(t => t.PersonId == id), await Blocker(db, person, BrusselsTime.Today(time))));
    }

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        if (await Blocker(db, person, BrusselsTime.Today(time)) is { } reason) return Problem(409, reason);

        await PersonDeleter.DeleteAsync(db, person);
        return Results.NoContent();
    }

    // Null means the Person can be deleted. Only the last Planner who can log in cannot.
    private static async Task<string?> Blocker(HeadroomDbContext db, Person person, DateOnly today) =>
        await LastPlanner.IsAsync(db, person, today) ? LastPlanner.Message : null;
}
