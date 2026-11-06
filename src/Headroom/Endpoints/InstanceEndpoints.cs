using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

public static class InstanceEndpoints
{
    public static void MapInstanceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/instance").RequireAuthorization();
        group.MapGet("/", GetInstance);
    }

    private static async Task<IResult> GetInstance(HeadroomDbContext db)
    {
        var settings = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync();
        return settings is null
            ? Results.NotFound()
            : Results.Ok(new InstanceResponse("Instance", settings.Id, settings.CompanyName));
    }
}
