namespace Headroom.Client.Api;

// Shared API types. Field names are a contract.
// Type is a DayOffTypeName. A public holiday has no Id and cannot change.
public record DayOffResponse(string Type, Guid? Id, DateOnly Date, string Name);

public record DayOffRequest(DateOnly Date, string Name);
