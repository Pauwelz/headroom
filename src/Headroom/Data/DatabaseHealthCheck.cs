using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headroom.Data;

// Healthy when the database answers. Returns no details.
public sealed class DatabaseHealthCheck(HeadroomDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
}
