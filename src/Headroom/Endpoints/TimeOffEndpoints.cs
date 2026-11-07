using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Time off in the Schedule view. A Person writes their own, a Planner everyone's. TimeOffWrites enforces it.
public static class TimeOffEndpoints
{
    public static void MapTimeOffEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("").RequireAuthorization();
        group.MapPost("/people/{personId:guid}/time-off", Add);
        group.MapPut("/time-off/{id:guid}", Edit);
        group.MapDelete("/time-off/{id:guid}", Delete);
    }

    private static async Task<IResult> Add(Guid personId, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await TimeOffWrites.SaveAsync(db, http.User, null, personId, request));

    private static async Task<IResult> Edit(Guid id, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await TimeOffWrites.ChangeAsync(db, http.User, id, request));

    private static async Task<IResult> Delete(Guid id, HttpContext http, HeadroomDbContext db) =>
        await TimeOffWrites.DeleteAsync(db, http.User, id) is { } refusal ? Problem(refusal) : Results.NoContent();

    private static IResult Saved((ScheduleTimeOff? TimeOff, Refusal? Refusal) result) =>
        result.Refusal is { } refusal ? Problem(refusal) : Results.Ok(result.TimeOff);
}
