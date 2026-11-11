using System.Security.Claims;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Capacity;

/// <summary>Time off writes with their rules. REST and MCP both call this. Exactly one of the two results is set.</summary>
public static class TimeOffWrites
{
    private static readonly Refusal NotFound = new(404, "The Time off or the Person does not exist.");
    private static readonly Refusal NotYours = new(403, "You can only change your own Time off.");

    // The one access rule: a Planner writes Time off of every Person, anyone else only their own.
    private static bool MayWrite(ClaimsPrincipal actor, Guid personId) =>
        actor.IsInRole(PersonPrincipal.PlannerRole) || actor.PersonId() == personId;

    public static async Task<(ScheduleTimeOff? TimeOff, Refusal? Refusal)> ChangeAsync(HeadroomDbContext db, ClaimsPrincipal actor, Guid id, TimeOffRequest request)
    {
        var personId = await db.TimeOff.Where(t => t.Id == id).Select(t => (Guid?)t.PersonId).FirstOrDefaultAsync();
        return personId is null ? (null, NotFound) : await SaveAsync(db, actor, id, personId.Value, request);
    }

    public static async Task<Refusal?> DeleteAsync(HeadroomDbContext db, ClaimsPrincipal actor, Guid id)
    {
        var timeOff = await db.TimeOff.FindAsync(id);
        if (timeOff is null) return NotFound;
        if (!MayWrite(actor, timeOff.PersonId)) return NotYours;
        db.TimeOff.Remove(timeOff);
        await db.SaveChangesAsync();
        return null;
    }

    // Create (id null) or change one Time off. Other Time off of the Person that overlaps or touches it merges in.
    public static async Task<(ScheduleTimeOff? TimeOff, Refusal? Refusal)> SaveAsync(
        HeadroomDbContext db, ClaimsPrincipal actor, Guid? id, Guid personId, TimeOffRequest request)
    {
        if (!MayWrite(actor, personId)) return (null, NotYours);
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null) return (null, NotFound);
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
        timeOff.LastChangedBy = actor.PersonId();
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
