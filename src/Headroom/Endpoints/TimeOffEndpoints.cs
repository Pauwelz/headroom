using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Time off in the Schedule view. Planners only: the server enforces it.
public static class TimeOffEndpoints
{
    public static void MapTimeOffEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("").RequirePlanner();
        group.MapPost("/people/{personId:guid}/time-off", Add);
        group.MapPut("/time-off/{id:guid}", Edit);
        group.MapDelete("/time-off/{id:guid}", Delete);
    }

    private static async Task<IResult> Add(Guid personId, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await SaveAsync(db, http.User.PersonId(), null, personId, request));

    private static async Task<IResult> Edit(Guid id, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await ChangeAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db) =>
        await DeleteAsync(db, id) is { } refusal ? Problem(refusal) : Results.NoContent();

    private static IResult Saved((ScheduleTimeOff? TimeOff, Refusal? Refusal) result) =>
        result.Refusal is { } refusal ? Problem(refusal) : Results.Ok(result.TimeOff);

    // The rules below are shared with the MCP tools. Exactly one of the two results is set.
    public static async Task<(ScheduleTimeOff? TimeOff, Refusal? Refusal)> ChangeAsync(HeadroomDbContext db, Guid? by, Guid id, TimeOffRequest request)
    {
        var personId = await db.TimeOff.Where(t => t.Id == id).Select(t => (Guid?)t.PersonId).FirstOrDefaultAsync();
        return personId is null ? (null, NotFound) : await SaveAsync(db, by, id, personId.Value, request);
    }

    public static async Task<Refusal?> DeleteAsync(HeadroomDbContext db, Guid id)
    {
        var timeOff = await db.TimeOff.FindAsync(id);
        if (timeOff is null) return NotFound;
        db.TimeOff.Remove(timeOff);
        await db.SaveChangesAsync();
        return null;
    }

    private static readonly Refusal NotFound = new(404, "The Time off or the Person does not exist.");

    // Create (id null) or change one Time off. Other Time off of the Person that overlaps or touches it merges in.
    public static async Task<(ScheduleTimeOff? TimeOff, Refusal? Refusal)> SaveAsync(
        HeadroomDbContext db, Guid? by, Guid? id, Guid personId, TimeOffRequest request)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null || person.ErasedNumber is not null) return (null, NotFound);
        if (!TryParse(request, out var requested) || !TimeOffRules.IsValid(requested))
            return (null, new Refusal(400, "Time off must end after it starts."));

        var others = await db.TimeOff.Where(t => t.PersonId == personId && t.Id != id).ToListAsync();
        var (term, merged) = TimeOffRules.Merge(requested, others, t => t.Term);
        if (person.EndDate is { } last && term.EndDate > last)
            return (null, new Refusal(400, $"Time off cannot cover a day after the end date ({DisplayDate.Format(last)})."));

        var timeOff = id is null ? db.TimeOff.Add(new TimeOff { PersonId = personId }).Entity : (await db.TimeOff.FindAsync(id))!;
        timeOff.StartDate = term.StartDate;
        timeOff.StartPart = term.StartPart;
        timeOff.EndDate = term.EndDate;
        timeOff.EndPart = term.EndPart;
        timeOff.LastChangedBy = by;
        db.TimeOff.RemoveRange(merged);
        await db.SaveChangesAsync();
        return (new ScheduleTimeOff(timeOff.Id, term.StartDate, DayPartNames.ToApi(term.StartPart), term.EndDate, DayPartNames.ToApi(term.EndPart)), null);
    }

    private static bool TryParse(TimeOffRequest r, out TimeOffTerm term)
    {
        term = default!;
        if (!DayPartNames.TryParse(r.StartPart, out var start) || !DayPartNames.TryParse(r.EndPart, out var end)) return false;
        term = new TimeOffTerm(r.StartDate, start, r.EndDate, end);
        return true;
    }
}
