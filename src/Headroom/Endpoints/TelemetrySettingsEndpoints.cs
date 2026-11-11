using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// The telemetry privacy values in Settings. Planners only, and only with the login cookie (ADR 0017).
public static class TelemetrySettingsEndpoints
{
    public static void MapTelemetrySettingsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings/telemetry").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        group.MapGet("/", Get);
        group.MapPut("/", Save);
    }

    private static async Task<IResult> Get(HeadroomDbContext db, TelemetryPrivacy privacy, IConfiguration configuration, CancellationToken ct)
    {
        var (endUserId, clientAddress) = await privacy.GetAsync(db, ct);
        return Results.Ok(new TelemetrySettingsResponse(endUserId, clientAddress, HeadroomTelemetry.ExporterSet(configuration)));
    }

    private static async Task<IResult> Save(
        TelemetrySettingsRequest request, HttpContext http, HeadroomDbContext db, TelemetryPrivacy privacy, CancellationToken ct)
    {
        var row = await db.InstanceSettings.FirstAsync(ct);
        (row.TelemetryEndUserId, row.TelemetryClientAddress) = (request.SendPersonId, request.SendClientAddress);
        row.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync(ct);
        privacy.Clear();
        return Results.NoContent();
    }
}
