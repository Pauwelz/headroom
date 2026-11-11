namespace Headroom.Client.Api;

public static class EmailServerDefaults
{
    public const int Port = 587;
}

/// <summary>Shared API types of /api/settings/email-server. No response holds the password.
/// An empty Password keeps the stored one.</summary>
public record EmailServerRequest(string Host, int Port, string? Username, string? Password, string FromAddress);

/// <summary>UsesEnvironment is true when nothing is stored and the Operator set Smtp__Host.</summary>
public record EmailServerResponse(string? Host, int Port, string? Username, string? FromAddress, bool HasPassword, bool UsesEnvironment)
{
    public string Type => "EmailServer";
}
