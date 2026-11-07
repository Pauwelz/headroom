using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Allocations;

/// <summary>The values that always travel together when an Allocation is placed.</summary>
public sealed record AllocationPlacement(Guid PersonId, Guid ProjectId, DateOnly Start, DateOnly End, int Percentage);

/// <summary>Allocation writes with their rules. REST and MCP both call this. A null result means the change is done.</summary>
public static class AllocationWrites
{
    private static readonly Refusal NotFound = new(404, "The Allocation does not exist.");

    public static async Task<Refusal?> CreateAsync(HeadroomDbContext db, Guid? by, CreateAllocationRequest request)
    {
        var placement = new AllocationPlacement(request.PersonId, request.ProjectId, request.StartDate, request.EndDate, request.Percentage);
        if (await Check(db, placement) is { } refusal) return refusal;
        await Place(db, by, null, placement);
        return null;
    }

    // Edit: dates, percentage, and optionally Person and Project. It stays the same Allocation.
    public static async Task<Refusal?> UpdateAsync(HeadroomDbContext db, Guid? by, Guid id, UpdateAllocationRequest request)
    {
        if (await db.Allocations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) is not { } current) return NotFound;
        if (await ArchivedProblem(db, current.ProjectId) is { } archived) return archived;
        var placement = new AllocationPlacement(
            request.PersonId ?? current.PersonId, request.ProjectId ?? current.ProjectId, request.StartDate, request.EndDate, request.Percentage);
        if (await Check(db, placement) is { } refusal) return refusal;
        await Place(db, by, id, placement);
        return null;
    }

    public static async Task<Refusal?> DeleteAsync(HeadroomDbContext db, Guid id)
    {
        if (await db.Allocations.FindAsync(id) is not { } allocation) return NotFound;
        if (await ArchivedProblem(db, allocation.ProjectId) is { } archived) return archived;
        db.Allocations.Remove(allocation);
        await db.SaveChangesAsync();
        return null;
    }

    // Split here: the first part keeps the id and ends the day before the date. The second part is new.
    public static async Task<Refusal?> SplitAsync(HeadroomDbContext db, Guid? by, Guid id, SplitAllocationRequest request)
    {
        if (await db.Allocations.FindAsync(id) is not { } allocation) return NotFound;
        var placement = new AllocationPlacement(allocation.PersonId, allocation.ProjectId, allocation.StartDate, allocation.EndDate, allocation.Percentage);
        if (await Check(db, placement) is { } refusal) return refusal;
        if (request.Date <= allocation.StartDate || request.Date > allocation.EndDate)
            return new Refusal(400, "The split date must be after the start date and not after the end date.");
        db.Allocations.Add(new Allocation
        {
            PersonId = allocation.PersonId, ProjectId = allocation.ProjectId, StartDate = request.Date,
            EndDate = allocation.EndDate, Percentage = allocation.Percentage, LastChangedBy = by,
        });
        (allocation.EndDate, allocation.LastChangedBy) = (request.Date.AddDays(-1), by);
        await db.SaveChangesAsync();
        return null;
    }

    private static async Task<Refusal?> ArchivedProblem(HeadroomDbContext db, Guid projectId) =>
        await db.Projects.FindAsync(projectId) is { Status: ProjectStatus.Archived }
            ? new Refusal(409, "An Archived Project accepts no Allocation changes. Unarchive it first.")
            : null;

    // Returns the refusal, or null when the change is allowed.
    private static async Task<Refusal?> Check(HeadroomDbContext db, AllocationPlacement p)
    {
        if (!AllocationRules.IsValidPercentage(p.Percentage)) return new Refusal(400, "The percentage is 5 to 100 in steps of 5.");
        if (p.End < p.Start) return new Refusal(400, "The end date is before the start date.");
        if (await db.People.FindAsync(p.PersonId) is not { ErasedNumber: null } person) return new Refusal(400, "The Person does not exist.");
        if (await db.Projects.FindAsync(p.ProjectId) is not { } project) return new Refusal(400, "The Project does not exist.");
        if (await ArchivedProblem(db, project.Id) is { } archived) return archived;
        if (person.EndDate is { } last && p.End > last)
            return new Refusal(400, $"An Allocation cannot cover a day after the end date of the Person ({DisplayDate.Format(last)}).");
        return null;
    }

    // The latest change wins on its days. One save makes the whole change atomic.
    private static async Task Place(HeadroomDbContext db, Guid? by, Guid? changedId, AllocationPlacement p)
    {
        var current = await db.Allocations.Where(a => a.PersonId == p.PersonId && a.ProjectId == p.ProjectId).ToListAsync();
        // An edit that changes Person or Project: the changed Allocation is not in the target group yet.
        if (changedId is { } id && current.All(a => a.Id != id) && await db.Allocations.FindAsync(id) is { } moved) current.Add(moved);
        var slices = AllocationRules.Place(
            current.Select(a => new AllocationSlice(a.Id, a.StartDate, a.EndDate, a.Percentage)), changedId, p.Start, p.End, p.Percentage);

        db.Allocations.RemoveRange(current.Where(a => slices.All(s => s.Id != a.Id)));
        foreach (var slice in slices)
        {
            var allocation = current.FirstOrDefault(a => a.Id == slice.Id);
            if (allocation is null)
            {
                allocation = new Allocation { PersonId = p.PersonId, ProjectId = p.ProjectId };
                db.Allocations.Add(allocation);
            }
            else if (allocation.PersonId == p.PersonId && allocation.ProjectId == p.ProjectId && allocation.StartDate == slice.Start
                     && allocation.EndDate == slice.End && allocation.Percentage == slice.Percentage)
                continue;
            (allocation.PersonId, allocation.ProjectId, allocation.StartDate, allocation.EndDate, allocation.Percentage, allocation.LastChangedBy) =
                (p.PersonId, p.ProjectId, slice.Start, slice.End, slice.Percentage, by);
        }
        await db.SaveChangesAsync();
    }
}
