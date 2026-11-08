using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Capacity;

/// <summary>Company day off writes with their rules. REST and MCP both call this. Exactly one of the two results is set.</summary>
public static class CompanyDaysOffWrites
{
    private static readonly Refusal Duplicate = new(409, "This date is already a Company day off.");
    private static readonly Refusal NotFound = new(404, "The Company day off does not exist.");

    public static async Task<(DayOffResponse? Day, Refusal? Refusal)> AddAsync(HeadroomDbContext db, Guid? actorId, DayOffRequest request)
    {
        if (Validate(request) is { } invalid) return (null, invalid);
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date)) return (null, Duplicate);
        var day = new CompanyDayOff { Date = request.Date, Name = request.Name.Trim(), LastChangedBy = actorId };
        db.CompanyDaysOff.Add(day);
        await db.SaveChangesAsync();
        return (ToResponse(day), null);
    }

    public static async Task<(DayOffResponse? Day, Refusal? Refusal)> ChangeAsync(HeadroomDbContext db, Guid? actorId, Guid id, DayOffRequest request)
    {
        if (Validate(request) is { } invalid) return (null, invalid);
        var day = await db.CompanyDaysOff.FindAsync(id);
        if (day is null) return (null, NotFound);
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date && d.Id != id)) return (null, Duplicate);
        day.Date = request.Date;
        day.Name = request.Name.Trim();
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

    private static Refusal? Validate(DayOffRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            ? new Refusal(400, "The name is required and has at most 200 characters.")
            : null;
}
