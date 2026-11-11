using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Headroom.Telemetry;

// Whether the Person id and the client IP address go into telemetry (ADR 0016, ADR 0017).
public class TelemetryPrivacy(IMemoryCache cache, IConfiguration configuration)
{
    private const string CacheKey = "telemetry-privacy";

    // The stored value first, then Telemetry__*, then on.
    public static bool Effective(bool? stored, bool? environment) => stored ?? environment ?? true;

    // shortcut: a save clears the cache of this replica only. Other replicas follow within 30 seconds. Use a shared signal if that is too slow.
    public async Task<(bool EndUserId, bool ClientAddress)> GetAsync(HeadroomDbContext db, CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var row = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(ct); // none before the first run
            return (
                Effective(row?.TelemetryEndUserId, configuration.GetValue<bool?>(HeadroomTelemetry.EndUserIdKey)),
                Effective(row?.TelemetryClientAddress, configuration.GetValue<bool?>(HeadroomTelemetry.ClientAddressKey)));
        });

    public void Clear() => cache.Remove(CacheKey);
}
