using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Set, change and remove the end date of a Person. Planners only.
public static class EndDateEndpoints
{
    public static void MapEndDateEndpoints(this RouteGroupBuilder people)
    {
        people.MapGet("/{id:guid}/end-date-impact", Impact);
        people.MapPut("/{id:guid}/end-date", Set);
        people.MapDelete("/{id:guid}/end-date", Remove);
    }

    // The records after the end date, loaded as tracked entities. The preview only reads them. Set cuts them.
    private sealed record Cut(List<Allocation> Allocations, List<TimeOff> TimeOff, List<Contract> Contracts);

    private static async Task<IResult> Impact(Guid id, DateOnly endDate, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        if (await Refusal(db, person, endDate, BrusselsTime.Today(time)) is { } refusal) return refusal;

        var cut = await LoadCut(db, id, endDate);
        var names = await ProjectNames(db, cut.Allocations);
        var records = cut.Allocations
            .Select(a => new CutRecord("Allocation", $"{names[a.ProjectId]}, {DisplayDate.Format(a.StartDate)} to {DisplayDate.Format(a.EndDate)}, {a.Percentage}%", a.StartDate > endDate))
            .Concat(cut.TimeOff.Select(t => new CutRecord("Time off", $"{DisplayDate.Format(t.StartDate)} to {DisplayDate.Format(t.EndDate)}", t.StartDate > endDate)))
            .Concat(cut.Contracts.Select(c => new CutRecord("Contract", $"from {DisplayDate.Format(c.StartDate)}", true)))
            .ToList();
        return Results.Ok(new EndDateImpact(endDate, endDate.AddDays(1), records));
    }

    private static async Task<IResult> Set(
        Guid id, SetEndDateRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        var today = BrusselsTime.Today(time);
        var end = request.EndDate;
        if (await Refusal(db, person, end, today) is { } refusal) return refusal;

        // The cut is a system action: the records get the Planner who set the end date.
        var by = http.User.PersonId();
        var cut = await LoadCut(db, id, end);
        foreach (var a in cut.Allocations)
            if (a.StartDate > end) db.Allocations.Remove(a);
            else { a.EndDate = end; a.LastChangedBy = by; }
        foreach (var t in cut.TimeOff)
            if (t.StartDate > end) db.TimeOff.Remove(t);
            else { t.EndDate = end; t.EndPart = DayPart.Afternoon; t.LastChangedBy = by; }
        db.Contracts.RemoveRange(cut.Contracts);
        person.EndDate = end;
        person.LastChangedBy = by;
        await db.SaveChangesAsync();
        return Results.Ok(await PeopleEndpoints.ToResponseAsync(db, person, today));
    }

    private static async Task<IResult> Remove(Guid id, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        var today = BrusselsTime.Today(time);
        if (PersonLockRules.RemoveEndDate(person, today) is { } reason) return Problem(409, reason);

        person.EndDate = null;
        person.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync();
        return Results.Ok(await PeopleEndpoints.ToResponseAsync(db, person, today));
    }

    // Null means the end date is allowed.
    private static async Task<IResult?> Refusal(HeadroomDbContext db, Person person, DateOnly end, DateOnly today)
    {
        if (PersonLockRules.SetEndDate(person, await LastPlanner.IsAsync(db, person, today), today) is { } reason)
            return Problem(409, reason);
        var first = await db.Contracts.Where(c => c.PersonId == person.Id).MinAsync(c => (DateOnly?)c.StartDate);
        if (first > end) return Problem(400, "The end date is before the first Contract. Delete the Person instead.");
        return null;
    }

    private static async Task<Cut> LoadCut(HeadroomDbContext db, Guid personId, DateOnly end) => new(
        await db.Allocations.Where(a => a.PersonId == personId && a.EndDate > end).OrderBy(a => a.StartDate).ToListAsync(),
        await db.TimeOff.Where(t => t.PersonId == personId && t.EndDate > end).OrderBy(t => t.StartDate).ToListAsync(),
        await db.Contracts.Where(c => c.PersonId == personId && c.StartDate > end).OrderBy(c => c.StartDate).ToListAsync());

    private static async Task<Dictionary<Guid, string>> ProjectNames(HeadroomDbContext db, List<Allocation> allocations)
    {
        var ids = allocations.Select(a => a.ProjectId).Distinct().ToList();
        var rows = await (from p in db.Projects join c in db.Clients on p.ClientId equals c.Id where ids.Contains(p.Id)
                          select new { p.Id, ClientName = c.Name, ProjectName = p.Name }).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => ProjectLabel.Format(r.ClientName, r.ProjectName));
    }
}
