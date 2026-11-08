using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.People;

/// <summary>End date rules and the cut. REST and MCP both call this. Exactly one of the two results is set.</summary>
public static class EndDateWrites
{
    private static readonly Refusal NotFound = new(404, "The Person does not exist.");

    // The records after the end date, loaded as tracked entities. The preview only reads them. Set cuts them.
    private sealed record Cut(List<Allocation> Allocations, List<TimeOff> TimeOff, List<Contract> Contracts);

    public static async Task<(EndDateImpact? Impact, Refusal? Refusal)> ImpactAsync(HeadroomDbContext db, Guid id, DateOnly endDate, DateOnly today)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return (null, NotFound);
        if (await RefuseAsync(db, person, endDate, today) is { } refusal) return (null, refusal);

        var cut = await LoadCut(db, id, endDate);
        var names = await ProjectNames(db, cut.Allocations);
        var records = cut.Allocations
            .Select(a => new CutRecord("Allocation", $"{names[a.ProjectId]}, {DisplayDate.Format(a.StartDate)} to {DisplayDate.Format(a.EndDate)}, {a.Percentage}%", a.StartDate > endDate))
            .Concat(cut.TimeOff.Select(t => new CutRecord("Time off", $"{DisplayDate.Format(t.StartDate)} to {DisplayDate.Format(t.EndDate)}", t.StartDate > endDate)))
            .Concat(cut.Contracts.Select(c => new CutRecord("Contract", $"from {DisplayDate.Format(c.StartDate)}", true)))
            .ToList();
        return (new EndDateImpact(endDate, endDate.AddDays(1), records), null);
    }

    public static async Task<(Person? Person, Refusal? Refusal)> SetAsync(HeadroomDbContext db, Guid by, Guid id, DateOnly end, DateOnly today)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return (null, NotFound);
        if (await RefuseAsync(db, person, end, today) is { } refusal) return (null, refusal);

        // The cut is a system action: the records get the Planner who set the end date.
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
        return (person, null);
    }

    public static async Task<(Person? Person, Refusal? Refusal)> RemoveAsync(HeadroomDbContext db, Guid by, Guid id, DateOnly today)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return (null, NotFound);
        if (PersonLockRules.RemoveEndDate(person, today) is { } reason) return (null, new Refusal(409, reason));

        person.EndDate = null;
        person.LastChangedBy = by;
        await db.SaveChangesAsync();
        return (person, null);
    }

    // Null means the end date is allowed.
    private static async Task<Refusal?> RefuseAsync(HeadroomDbContext db, Person person, DateOnly end, DateOnly today)
    {
        if (PersonLockRules.SetEndDate(person, await LastPlanner.IsAsync(db, person, today), today) is { } reason)
            return new Refusal(409, reason);
        var first = await db.Contracts.Where(c => c.PersonId == person.Id).MinAsync(c => (DateOnly?)c.StartDate);
        if (first > end) return new Refusal(400, "The end date is before the first Contract. Delete the Person instead.");
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
