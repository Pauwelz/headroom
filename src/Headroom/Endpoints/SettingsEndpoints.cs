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

    private static async Task<IResult> ListYear(int year, HeadroomDbContext db, IPublicHolidays holidays)
    {
        var (days, refusal) = await ListYearAsync(year, db, holidays);
        return refusal is null ? Results.Ok(days) : PlainText(refusal);
    }

    // Company days off and Belgian public holidays of one year, by date.
    public static async Task<(List<DayOffResponse>? Days, Refusal? Refusal)> ListYearAsync(int year, HeadroomDbContext db, IPublicHolidays holidays)
    {
        if (year is < 1900 or > 2200) return (null, new Refusal(400, "Year out of range."));
        var own = await db.CompanyDaysOff.AsNoTracking()
            .Where(d => d.Date.Year == year)
            .Select(d => new DayOffResponse(DayOffTypeName.CompanyDayOff, d.Id, d.Date, d.Name))
            .ToListAsync();
        var all = own.Concat(holidays.InYear(year).Select(h => new DayOffResponse(DayOffTypeName.PublicHoliday, null, h.Key, h.Value)));
        return (all.OrderBy(d => d.Date).ThenBy(d => d.Type).ToList(), null);
    }

    private static async Task<IResult> Add(DayOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await CompanyDaysOffWrites.AddAsync(db, http.User.PersonId(), request));

    private static async Task<IResult> Edit(Guid id, DayOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await CompanyDaysOffWrites.ChangeAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db) =>
        await CompanyDaysOffWrites.DeleteAsync(db, id) is { } refusal ? PlainText(refusal) : Results.NoContent();

    private static IResult Saved((DayOffResponse? Day, Refusal? Refusal) result) =>
        result.Refusal is { } refusal ? PlainText(refusal) : Results.Ok(result.Day);

    // These endpoints always sent plain-text bodies, not problem details. Integrators rely on them.
    private static IResult PlainText(Refusal refusal) => refusal.Status switch
    {
        404 => Results.NotFound(),
        409 => Results.Conflict(refusal.Detail),
        _ => Results.BadRequest(refusal.Detail),
    };
}
