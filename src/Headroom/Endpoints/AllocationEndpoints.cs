using Headroom.Allocations;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Allocation writes. Only a Planner writes. Reads come with GET /api/schedule.
public static class AllocationEndpoints
{
    public static void MapAllocationEndpoints(this RouteGroupBuilder api)
    {
        var allocations = api.MapGroup("/allocations").RequireAuthorization().RequirePlanner();
        allocations.MapPost("", Create);
        allocations.MapPut("/{id:guid}", Update);
        allocations.MapPost("/{id:guid}/split", Split);
        allocations.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> Create(CreateAllocationRequest request, HeadroomDbContext db, HttpContext http) =>
        Result(await CreateAsync(db, http.User.PersonId(), request));

    private static async Task<IResult> Update(Guid id, UpdateAllocationRequest request, HeadroomDbContext db, HttpContext http) =>
        Result(await UpdateAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db) => Result(await DeleteAsync(db, id));

    private static IResult Result(Refusal? refusal) => refusal is null ? Results.NoContent() : Problem(refusal);

    // The rules below are shared with the MCP tools. A null result means the change is done.
    public static async Task<Refusal?> CreateAsync(HeadroomDbContext db, Guid? by, CreateAllocationRequest request)
    {
        if (await Check(db, request.PersonId, request.ProjectId, request.StartDate, request.EndDate, request.Percentage) is { } refusal) return refusal;
        await Place(db, by, null, request.PersonId, request.ProjectId, request.StartDate, request.EndDate, request.Percentage);
        return null;
    }

    // Edit: dates, percentage, and optionally Person and Project. It stays the same Allocation.
    public static async Task<Refusal?> UpdateAsync(HeadroomDbContext db, Guid? by, Guid id, UpdateAllocationRequest request)
    {
        if (await db.Allocations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id) is not { } current) return NotFound;
        if (await ArchivedProblem(db, current.ProjectId) is { } archived) return archived;
        var personId = request.PersonId ?? current.PersonId;
        var projectId = request.ProjectId ?? current.ProjectId;
        if (await Check(db, personId, projectId, request.StartDate, request.EndDate, request.Percentage) is { } refusal) return refusal;
        await Place(db, by, id, personId, projectId, request.StartDate, request.EndDate, request.Percentage);
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

    private static readonly Refusal NotFound = new(404, "The Allocation does not exist.");

    // Split here: the first part keeps the id and ends the day before the date. The second part is new.
    private static async Task<IResult> Split(Guid id, SplitAllocationRequest request, HeadroomDbContext db, HttpContext http)
    {
        if (await db.Allocations.FindAsync(id) is not { } allocation) return Problem(NotFound);
        if (await ArchivedProblem(db, allocation.ProjectId) is { } archived) return Problem(archived);
        if (request.Date <= allocation.StartDate || request.Date > allocation.EndDate)
            return Problem(400, "The split date must be after the start date and not after the end date.");
        var by = http.User.PersonId();
        db.Allocations.Add(new Allocation
        {
            PersonId = allocation.PersonId, ProjectId = allocation.ProjectId, StartDate = request.Date,
            EndDate = allocation.EndDate, Percentage = allocation.Percentage, LastChangedBy = by,
        });
        (allocation.EndDate, allocation.LastChangedBy) = (request.Date.AddDays(-1), by);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<Refusal?> ArchivedProblem(HeadroomDbContext db, Guid projectId) =>
        await db.Projects.FindAsync(projectId) is { Status: ProjectStatus.Archived }
            ? new Refusal(409, "An Archived Project accepts no Allocation changes. Unarchive it first.")
            : null;

    // Returns the problem to send, or null when the change is allowed.
    private static async Task<Refusal?> Check(
        HeadroomDbContext db, Guid personId, Guid projectId, DateOnly start, DateOnly end, int percentage)
    {
        if (!AllocationRules.IsValidPercentage(percentage)) return new Refusal(400, "The percentage is 5 to 100 in steps of 5.");
        if (end < start) return new Refusal(400, "The end date is before the start date.");
        if (await db.People.FindAsync(personId) is not { ErasedNumber: null } person) return new Refusal(400, "The Person does not exist.");
        if (await db.Projects.FindAsync(projectId) is not { } project) return new Refusal(400, "The Project does not exist.");
        if (await ArchivedProblem(db, project.Id) is { } archived) return archived;
        if (person.EndDate is { } last && end > last)
            return new Refusal(400, $"An Allocation cannot cover a day after the end date of the Person ({DisplayDate.Format(last)}).");
        return null;
    }

    // The latest change wins on its days. One save makes the whole change atomic.
    private static async Task Place(
        HeadroomDbContext db, Guid? by, Guid? changedId, Guid personId, Guid projectId, DateOnly start, DateOnly end, int percentage)
    {
        var current = await db.Allocations.Where(a => a.PersonId == personId && a.ProjectId == projectId).ToListAsync();
        // An edit that changes Person or Project: the changed Allocation is not in the target group yet.
        if (changedId is { } id && current.All(a => a.Id != id) && await db.Allocations.FindAsync(id) is { } moved) current.Add(moved);
        var slices = AllocationRules.Place(
            current.Select(a => new AllocationSlice(a.Id, a.StartDate, a.EndDate, a.Percentage)), changedId, start, end, percentage);

        db.Allocations.RemoveRange(current.Where(a => slices.All(s => s.Id != a.Id)));
        foreach (var slice in slices)
        {
            var allocation = current.FirstOrDefault(a => a.Id == slice.Id);
            if (allocation is null)
            {
                allocation = new Allocation { PersonId = personId, ProjectId = projectId };
                db.Allocations.Add(allocation);
            }
            else if (allocation.PersonId == personId && allocation.ProjectId == projectId && allocation.StartDate == slice.Start
                     && allocation.EndDate == slice.End && allocation.Percentage == slice.Percentage)
                continue;
            (allocation.PersonId, allocation.ProjectId, allocation.StartDate, allocation.EndDate, allocation.Percentage, allocation.LastChangedBy) =
                (personId, projectId, slice.Start, slice.End, slice.Percentage, by);
        }
        await db.SaveChangesAsync();
    }
}
