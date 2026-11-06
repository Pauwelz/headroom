using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Schedule;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Schedule view. Every logged-in Person may read it. Allocation and Time off writes live in their own endpoint groups.
public static class ScheduleEndpoints
{
    // The view loads a 3-month range plus 3 weeks on each side: 134 days at most. This limit guards against unbounded requests.
    // It is not a performance limit.
    private const int MaxDays = 140;

    public static void MapScheduleEndpoints(this RouteGroupBuilder api) =>
        api.MapGet("/schedule", Get).RequireAuthorization();

    private static async Task<IResult> Get(DateOnly from, DateOnly to, HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time)
    {
        var (schedule, error) = await QueryAsync(from, to, db, holidays, time);
        return schedule is null ? Problem(400, error!) : Results.Ok(schedule);
    }

    // The endpoint and the MCP tool share this query. The error is a message for the caller.
    public static async Task<(ScheduleResponse? Schedule, string? Error)> QueryAsync(
        DateOnly from, DateOnly to, HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
            return (null, $"Ask for 1 to {MaxDays} days.");

        var (inputs, companyDays) = await ScheduleInputs.LoadAsync(db, from, to, BrusselsTime.Today(time), includeErased: false);
        return (ScheduleBuilder.Build(from, to, inputs, holidays, companyDays), null);
    }
}
