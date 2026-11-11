namespace Headroom.Data;

public class InstanceSettings : Record
{
    // Set on first run.
    public string? CompanyName { get; set; }

    // The last erased number given out. The next erase takes one more.
    public int ErasedCount { get; set; }

    // The email server a Planner sets. A null host means nothing is stored, and Smtp__* applies.
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }

    // Data Protection ciphertext, never plain text.
    public string? SmtpPassword { get; set; }
    public string? SmtpFromAddress { get; set; }

    // The telemetry privacy values a Planner sets. Null means nothing is stored, and Telemetry__* applies.
    public bool? TelemetryEndUserId { get; set; }
    public bool? TelemetryClientAddress { get; set; }
}
