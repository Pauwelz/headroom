using Headroom.Auth;
using Headroom.Data;
using Headroom.Events;

namespace Headroom.People;

// Runs the automatic erase and the Event sweep at startup and on the first request of each day.
// The app scales to zero, so there is no timer.
public sealed class ErasureCheck(IServiceScopeFactory scopes, TimeProvider time, ILogger<ErasureCheck> log)
{
    private readonly Lock _gate = new();
    private DateOnly? _lastRun;

    public async Task RunIfDueAsync()
    {
        var today = BrusselsTime.Today(time);
        lock (_gate)
        {
            if (_lastRun == today) return;
            _lastRun = today;
        }
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            await PersonEraser.EraseDueAsync(db, today);
            await EventRetention.DeleteExpiredAsync(db, time.GetUtcNow());
        }
        catch (Exception e)
        {
            // A failed check must not fail the request. The next request tries again.
            log.LogError(e, "The automatic erase failed.");
            lock (_gate) _lastRun = null;
        }
    }
}
