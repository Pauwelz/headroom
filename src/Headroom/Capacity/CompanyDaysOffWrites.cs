using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Capacity;

/// <summary>Company day off writes with their rules. REST and MCP both call this. Exactly one of the two results is set.</summary>
public static class CompanyDaysOffWrites
{
    private static readonly Refusal InvalidName = new(400, "The name is required and has at most 200 characters.");
    private static readonly Refusal Duplicate = new(409, "This date is already a Company day off.");
    private static readonly Refusal NotFound = new(404, "The Company day off does not exist.");

    public static async Task<(DayOffResponse? Day, Refusal? Refusal)> AddAsync(HeadroomDbContext db, Guid? actorId, DayOffRequest request)
    {
        if (ApiProblem.CleanName(request.Name) is not { } name) return (null, InvalidName);
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date)) return (null, Duplicate);
        var day = new CompanyDayOff { Date = request.Date, Name = name, LastChangedBy = actorId };
        db.CompanyDaysOff.Add(day);
        await db.SaveChangesAsync();
        return (ToResponse(day), null);
    }

    public static async Task<(DayOffResponse? Day, Refusal? Refusal)> ChangeAsync(HeadroomDbContext db, Guid? actorId, Guid id, DayOffRequest request)
    {
        if (ApiProblem.CleanName(request.Name) is not { } name) return (null, InvalidName);
        var day = await db.CompanyDaysOff.FindAsync(id);
        if (day is null) return (null, NotFound);
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date && d.Id != id)) return (null, Duplicate);
        day.Date = request.Date;
        day.Name = name;
        day.LastChangedBy = actorId;
        await db.SaveChangesAsync();
        return (ToResponse(day), null);
    }

    public static async Task<Refusal?> DeleteAsync(HeadroomDbContext db, Guid id)
    {
        var day = await db.CompanyDaysOff.FindAsync(id);
        if (day is null) return NotFound;
        db.CompanyDaysOff.Remove(day);
        await db.SaveChangesAsync();
        return null;
    }

    private static DayOffResponse ToResponse(CompanyDayOff d) => new(DayOffTypeName.CompanyDayOff, d.Id, d.Date, d.Name);

}
