using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;
using static Headroom.Endpoints.PeopleEndpoints;

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
        return person is null ? Results.NotFound() : Results.Ok(new DeleteCheck(await Blocker(db, person, BrusselsTime.Today(time))));
    }

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        if (await Blocker(db, person, BrusselsTime.Today(time)) is { } reason) return Problem(409, reason);

        await Connections.DeleteOfPersonAsync(db, id);
        db.Contracts.RemoveRange(db.Contracts.Where(c => c.PersonId == id));
        db.People.Remove(person);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // Null means the Person can be deleted. Their own Contracts and own row do not count.
    private static async Task<string?> Blocker(HeadroomDbContext db, Person person, DateOnly today)
    {
        var id = person.Id;
        if (await db.Allocations.AnyAsync(a => a.PersonId == id)) return "An Allocation refers to this Person. Set an end date instead.";
        if (await db.TimeOff.AnyAsync(t => t.PersonId == id)) return "Time off refers to this Person. Set an end date instead.";
        // Every record type has "last changed by". Add a new record type here too.
        if (await db.Allocations.AnyAsync(r => r.LastChangedBy == id)
            || await db.TimeOff.AnyAsync(r => r.LastChangedBy == id)
            || await db.Contracts.AnyAsync(r => r.PersonId != id && r.LastChangedBy == id)
            || await db.People.AnyAsync(r => r.Id != id && r.LastChangedBy == id)
            || await db.Clients.AnyAsync(r => r.LastChangedBy == id)
            || await db.Projects.AnyAsync(r => r.LastChangedBy == id)
            || await db.CompanyDaysOff.AnyAsync(r => r.LastChangedBy == id)
            || await db.InstanceSettings.AnyAsync(r => r.LastChangedBy == id))
            return "A record shows this Person as \"last changed by\". Set an end date instead.";
        return await LastPlanner.IsAsync(db, person, today) ? LastPlanner.Message : null;
    }
}
