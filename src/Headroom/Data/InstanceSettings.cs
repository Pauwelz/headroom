using Headroom.Client.Api;

namespace Headroom.Data;

public class InstanceSettings : Record
{
    // Set on first run.
    public string? CompanyName { get; set; }

    // Unused since Delete replaced Erase. A later release drops the column.
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

    // The Company login a Planner sets. A null kind means there is none.
    public CompanyLoginKind? CompanyLoginKind { get; set; }
    public string? CompanyLoginIssuer { get; set; }
    public string? CompanyLoginClientId { get; set; }

    // Data Protection ciphertext, never plain text.
    public string? CompanyLoginClientSecret { get; set; }

    // While the switch is on, Company login is the only login for an email in one of the domains. Lower case, exact match.
    public bool CompanyLoginOnly { get; set; }
    public string[] CompanyDomains { get; set; } = [];
}
