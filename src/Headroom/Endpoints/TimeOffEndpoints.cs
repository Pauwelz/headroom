using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Time off in the Schedule view. Planners only: the server enforces it. The rules are in TimeOffWrites.
public static class TimeOffEndpoints
{
    public static void MapTimeOffEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("").RequirePlanner();
        group.MapPost("/people/{personId:guid}/time-off", Add);
        group.MapPut("/time-off/{id:guid}", Edit);
        group.MapDelete("/time-off/{id:guid}", Delete);
    }

    private static async Task<IResult> Add(Guid personId, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await TimeOffWrites.SaveAsync(db, http.User.PersonId(), null, personId, request));

    private static async Task<IResult> Edit(Guid id, TimeOffRequest request, HttpContext http, HeadroomDbContext db) =>
        Saved(await TimeOffWrites.ChangeAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db) =>
        await TimeOffWrites.DeleteAsync(db, id) is { } refusal ? Problem(refusal) : Results.NoContent();

    private static IResult Saved((ScheduleTimeOff? TimeOff, Refusal? Refusal) result) =>
        result.Refusal is { } refusal ? Problem(refusal) : Results.Ok(result.TimeOff);
}
