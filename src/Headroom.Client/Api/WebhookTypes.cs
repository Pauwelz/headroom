namespace Headroom.Client.Api;

// Shared API types of /api/settings/webhooks. The secret is only in the response to the create call.
public record WebhookRequest(string Name, string Url);

public record RenameWebhookRequest(string Name);

public record WebhookResponse(Guid Id, string Name, string Url)
{
    public string Type => "Webhook";
}

/// <summary>Secret is the whsec_ signing key. Headroom shows it once.</summary>
public record CreatedWebhookResponse(Guid Id, string Name, string Url, string Secret)
{
    public string Type => "Webhook";
}

/// <summary>A stored Event that was not delivered. GaveUp is true when Headroom no longer retries it.</summary>
public record FailedEventResponse(Guid Id, Guid WebhookId, string EventType, DateTimeOffset DateCreated, int Attempts, string LastError, bool GaveUp)
{
    public string Type => "Event";
}
