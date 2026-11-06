using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Settings page. Planners only.
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var days = api.MapGroup("/settings/company-days-off").RequirePlanner();
        days.MapGet("/", ListYear);
        days.MapPost("/", Add);
        days.MapPut("/{id:guid}", Edit);
        days.MapDelete("/{id:guid}", Delete);
    }

    // Company days off and Belgian public holidays of one year, by date.
    private static async Task<IResult> ListYear(int year, HeadroomDbContext db, IPublicHolidays holidays)
    {
        if (year is < 1900 or > 2200) return Results.BadRequest("Year out of range.");
        var own = await db.CompanyDaysOff.AsNoTracking()
            .Where(d => d.Date.Year == year)
            .Select(d => new DayOffResponse("CompanyDayOff", d.Id, d.Date, d.Name))
            .ToListAsync();
        var all = own.Concat(holidays.InYear(year).Select(h => new DayOffResponse("PublicHoliday", null, h.Key, h.Value)));
        return Results.Ok(all.OrderBy(d => d.Date).ThenBy(d => d.Type));
    }

    private static async Task<IResult> Add(DayOffRequest request, HttpContext http, HeadroomDbContext db)
    {
        if (Validate(request) is { } invalid) return invalid;
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date)) return Duplicate();
        var day = new CompanyDayOff { Date = request.Date, Name = request.Name.Trim(), LastChangedBy = http.User.PersonId() };
        db.CompanyDaysOff.Add(day);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(day));
    }

    private static async Task<IResult> Edit(Guid id, DayOffRequest request, HttpContext http, HeadroomDbContext db)
    {
        if (Validate(request) is { } invalid) return invalid;
        var day = await db.CompanyDaysOff.FindAsync(id);
        if (day is null) return Results.NotFound();
        if (await db.CompanyDaysOff.AnyAsync(d => d.Date == request.Date && d.Id != id)) return Duplicate();
        day.Date = request.Date;
        day.Name = request.Name.Trim();
        day.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(day));
    }

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db)
    {
        var day = await db.CompanyDaysOff.FindAsync(id);
        if (day is null) return Results.NotFound();
        db.CompanyDaysOff.Remove(day);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static DayOffResponse ToResponse(CompanyDayOff d) => new("CompanyDayOff", d.Id, d.Date, d.Name);

    private static IResult? Validate(DayOffRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            ? Results.BadRequest("The name is required and has at most 200 characters.")
            : null;

    private static IResult Duplicate() => Results.Conflict("This date is already a Company day off.");
}
