using Headroom.Allocations;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Allocation writes. Only a Planner writes. Reads come with GET /api/schedule. The rules are in AllocationWrites.
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
        Result(await AllocationWrites.CreateAsync(db, http.User.PersonId(), request));

    private static async Task<IResult> Update(Guid id, UpdateAllocationRequest request, HeadroomDbContext db, HttpContext http) =>
        Result(await AllocationWrites.UpdateAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Split(Guid id, SplitAllocationRequest request, HeadroomDbContext db, HttpContext http) =>
        Result(await AllocationWrites.SplitAsync(db, http.User.PersonId(), id, request));

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db) => Result(await AllocationWrites.DeleteAsync(db, id));

    private static IResult Result(Refusal? refusal) => refusal is null ? Results.NoContent() : Problem(refusal);
}
