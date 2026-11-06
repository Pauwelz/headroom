using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Schedule;
using Headroom.Utilisation;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Utilisation report. Every logged-in Person may read it.
public static class UtilisationEndpoints
{
    // The report asks for two quarters, at most 184 days. The limit adds a margin of 6 days and keeps a request small.
    private const int MaxDays = 190;

    public static void MapUtilisationEndpoints(this RouteGroupBuilder api) =>
        api.MapGet("/utilisation", Get).RequireAuthorization();

    private static async Task<IResult> Get(
        DateOnly from, DateOnly to, string period, HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time)
    {
        var (report, error) = await QueryAsync(from, to, period, db, holidays, time);
        return report is null ? Problem(400, error!) : Results.Ok(report);
    }

    // The endpoint and the MCP tool share this query. The error is a message for the caller.
    public static async Task<(UtilisationResponse? Report, string? Error)> QueryAsync(
        DateOnly from, DateOnly to, string period, HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
            return (null, $"Ask for 1 to {MaxDays} days.");
        if (period is not (UtilisationBuilder.Week or UtilisationBuilder.Month))
            return (null, "Period is 'week' or 'month'.");

        // A week at the edge can start before From and end after To. Load the full periods.
        var periods = UtilisationBuilder.Periods(from, to, period);
        var start = periods.Count > 0 ? DateOnly.FromDayNumber(Math.Min(from.DayNumber, periods[0].StartDate.DayNumber)) : from;
        var end = periods.Count > 0 ? DateOnly.FromDayNumber(Math.Max(to.DayNumber, periods[^1].EndDate.DayNumber)) : to;
        var (inputs, companyDays) = await ScheduleInputs.LoadAsync(db, start, end, BrusselsTime.Today(time), includeErased: true);
        return (UtilisationBuilder.Build(from, to, period, inputs, holidays, companyDays), null);
    }
}
