namespace Headroom.Client.Api;

// Shared API types. Field names are a contract.
// Type is a DayOffTypeName.
public record DayOffResponse(string Type, Guid? Id, DateOnly Date, string Name);

public record DayOffRequest(DateOnly Date, string Name);
