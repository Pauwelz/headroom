namespace Headroom.Client.Api;

// Shared API type. Field names are a contract.
public record InstanceResponse(string Type, Guid Id, string? Name);
