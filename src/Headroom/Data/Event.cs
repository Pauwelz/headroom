namespace Headroom.Data;

// One Event for one Webhook. The Id is the webhook-id header and the CloudEvents id.
// Payload is the CloudEvents JSON body, stored as sent. Delivery state: pending when DeliveredAt and LastError are null.
public class Event
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WebhookId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Sends started so far. A replica claims an Event by raising this number.</summary>
    public int Attempts { get; set; }
    /// <summary>No send before this time. Null means send now.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
