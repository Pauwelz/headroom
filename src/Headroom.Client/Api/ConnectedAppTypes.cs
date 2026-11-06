namespace Headroom.Client.Api;

// Shared API types of /api/settings/connected-apps. The client secret is only in the response to the create and new-secret calls.
public record ConnectedAppRequest(string Name, List<string> RedirectUrls);

public record ConnectedAppResponse(string Id, string Name, string ClientId, List<string> RedirectUrls)
{
    public string Type => "ConnectedApp";
}

/// <summary>ClientSecret is the secret of the Connected app. Headroom shows it once.</summary>
public record CreatedConnectedAppResponse(string Id, string Name, string ClientId, List<string> RedirectUrls, string ClientSecret)
{
    public string Type => "ConnectedApp";
}
