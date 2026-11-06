using System.Security.Cryptography;
using System.Text;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Events;

// Sends stored Events with Standard Webhooks headers. A failed Event keeps LastError and is retried with back-off until GiveUpAfter has passed (ADR 0004).
// A replica claims an Event with a conditional update before it sends, so two replicas do not send the same Event together.
public class EventSender(HeadroomDbContext db, IHttpClientFactory clients, TimeProvider time)
{
    public const string ClientName = "webhooks";
    public const string SecretPrefix = "whsec_";

    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    // Not delivered and not given up. It includes Events in back-off. The backlog gauge counts the same set.
    public static IQueryable<Event> Pending(IQueryable<Event> events, DateTimeOffset now)
    {
        var cutoff = now - GiveUpAfter;
        return events.Where(e => e.DeliveredAt == null && e.DateCreated > cutoff);
    }

    // Delivered and given-up Events stay this long, so a Webhook owner can debug a delivery.
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public static Task DeleteExpiredAsync(HeadroomDbContext db, DateTimeOffset now)
    {
        var cutoff = now - Retention;
        return db.Events.Where(e => e.DateCreated < cutoff && !Pending(db.Events, now).Select(p => p.Id).Contains(e.Id)).ExecuteDeleteAsync();
    }

    // Delay before the next send, after the given number of sends: 30 s, 1 min, 2 min, ... at most 1 hour.
    public static TimeSpan BackOff(int attempts) => TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, Math.Min(attempts - 1, 7)), 3600));

    public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public async Task SendPendingAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var pending = await Pending(db.Events, now)
            .Where(e => e.NextAttemptAt == null || e.NextAttemptAt <= now)
            .OrderBy(e => e.DateCreated).Take(100)
            .Join(db.Webhooks, e => e.WebhookId, w => w.Id, (e, w) => new { Event = e, w.Url, w.Secret })
            .ToListAsync(ct);
        var client = clients.CreateClient(ClientName);
        foreach (var item in pending)
        {
            var claimed = await db.Events
                .Where(e => e.Id == item.Event.Id && e.Attempts == item.Event.Attempts && e.DeliveredAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Attempts, item.Event.Attempts + 1)
                    .SetProperty(e => e.NextAttemptAt, now + BackOff(item.Event.Attempts + 1)), ct);
            if (claimed == 0) continue;
            try
            {
                using var request = BuildRequest(item.Event, item.Url, item.Secret);
                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) (item.Event.DeliveredAt, item.Event.LastError) = (time.GetUtcNow(), null);
                else item.Event.LastError = $"The receiver answered {(int)response.StatusCode}.";
                HeadroomMetrics.WebhookDelivery(response.IsSuccessStatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
            {
                item.Event.LastError = e.Message[..Math.Min(e.Message.Length, 500)];
                HeadroomMetrics.WebhookDelivery(false);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    // Standard Webhooks: sign "id.timestamp.body" with HMAC-SHA256. The key is the base64 text after "whsec_".
    private HttpRequestMessage BuildRequest(Event e, string url, string secret)
    {
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds().ToString();
        var signed = Encoding.UTF8.GetBytes($"{e.Id}.{timestamp}.{e.Payload}");
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Convert.FromBase64String(secret[SecretPrefix.Length..]), signed));
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(e.Payload, Encoding.UTF8, "application/cloudevents+json"),
        };
        request.Headers.Add("webhook-id", e.Id.ToString());
        request.Headers.Add("webhook-timestamp", timestamp);
        request.Headers.Add("webhook-signature", "v1," + signature);
        return request;
    }
}

// Sends stored Events while the app runs (ADR 0004).
public class EventSenderService(IServiceScopeFactory scopes, ILogger<EventSenderService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EventSender>().SendPendingAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Sending Events failed.");
            }
        }
    }
}
