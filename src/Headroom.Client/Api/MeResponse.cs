namespace Headroom.Client.Api;

// Shared API type. Field names are a contract.
public record MeResponse(string Type, Guid Id, string Name, string Email, bool IsPlanner);
