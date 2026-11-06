using System.Diagnostics.Metrics;

namespace Headroom.Telemetry;

// Aggregate counters. Every dimension has a small fixed set of values: never a Person, id, name or email.
// The names are stable: the Workbook and the alerts use them (operator guide, "Metrics").
public static class HeadroomMetrics
{
    private static readonly Meter Meter = new(HeadroomTelemetry.Name);

    private static readonly Counter<long> Logins = Meter.CreateCounter<long>("headroom.logins", "{login}");
    private static readonly Counter<long> LoginCodeEmails = Meter.CreateCounter<long>("headroom.login_code_emails", "{email}");
    private static readonly Counter<long> RecordChanges = Meter.CreateCounter<long>("headroom.record_changes", "{change}");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("headroom.requests", "{request}");
    private static readonly Counter<long> WebhookDeliveries = Meter.CreateCounter<long>("headroom.webhook_deliveries", "{delivery}");

    public static void Login(bool success) => Logins.Add(1, Result(success));

    public static void LoginCodeEmail(bool sent) => LoginCodeEmails.Add(1, Tag("result", sent ? "sent" : "failed"));

    public static void WebhookDelivery(bool success) => WebhookDeliveries.Add(1, Result(success));

    // The type is the Event type, such as headroom.Allocation.created: 7 values.
    public static void RecordChange(string eventType) => RecordChanges.Add(1, Tag("type", eventType));

    // Counts calls to /api and /mcp. Other paths are pages and assets.
    public static void Request(PathString path)
    {
        if (path.StartsWithSegments("/api")) Requests.Add(1, Tag("surface", "api"));
        else if (path.StartsWithSegments(Mcp.McpSetup.Path)) Requests.Add(1, Tag("surface", "mcp"));
    }

    private static KeyValuePair<string, object?> Result(bool success) => Tag("result", success ? "success" : "failed");

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);
}
