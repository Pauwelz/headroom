using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Events;

// Events stay this long, so a Webhook owner can debug a delivery.
// The period must be longer than EventSender.GiveUpAfter. Then the sweep never deletes a pending Event.
public static class EventRetention
{
    public static readonly TimeSpan Period = TimeSpan.FromDays(30);

    public static Task DeleteExpiredAsync(HeadroomDbContext db, DateTimeOffset now)
    {
        var cutoff = now - Period;
        return db.Events.Where(e => e.DateCreated < cutoff).ExecuteDeleteAsync();
    }
}
