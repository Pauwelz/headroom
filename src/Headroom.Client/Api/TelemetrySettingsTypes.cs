namespace Headroom.Client.Api;

/// <summary>Shared API types of /api/settings/telemetry.</summary>
public record TelemetrySettingsRequest(bool SendPersonId, bool SendClientAddress);

/// <summary>The two values are the effective ones. ExporterSet is false when the Operator set no exporter.</summary>
public record TelemetrySettingsResponse(bool SendPersonId, bool SendClientAddress, bool ExporterSet)
{
    public string Type => "TelemetrySettings";
}
