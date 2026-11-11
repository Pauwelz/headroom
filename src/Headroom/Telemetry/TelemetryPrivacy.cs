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
    public async Task<(bool EndUserId, bool ClientAddress)> GetAsync(HeadroomDbContext db, CancellationToken ct)
    {
        try
        {
            return await cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var row = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(ct); // none before the first run
                return From(row?.TelemetryEndUserId, row?.TelemetryClientAddress);
            });
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Telemetry must not fail a request. Without the database, use Telemetry__* or on, and cache nothing.
            return From(null, null);
        }
    }

    private (bool, bool) From(bool? endUserId, bool? clientAddress) => (
        Effective(endUserId, configuration.GetValue<bool?>(HeadroomTelemetry.EndUserIdKey)),
        Effective(clientAddress, configuration.GetValue<bool?>(HeadroomTelemetry.ClientAddressKey)));

    public void Clear() => cache.Remove(CacheKey);
}
