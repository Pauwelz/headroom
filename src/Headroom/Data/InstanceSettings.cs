namespace Headroom.Data;

public class InstanceSettings : Record
{
    // Set by a Planner in Settings. Null until then.
    public string? CompanyName { get; set; }

    // Unused since Delete replaced Erase. A later release drops the column.
    public int ErasedCount { get; set; }

    // The telemetry privacy values a Planner sets. Null means nothing is stored, and Telemetry__* applies.
    public bool? TelemetryEndUserId { get; set; }
    public bool? TelemetryClientAddress { get; set; }
}
