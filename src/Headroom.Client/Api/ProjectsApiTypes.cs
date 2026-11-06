namespace Headroom.Client.Api;

// Shared API types for the Projects page. Field names are a contract.
// Status is "tentative", "confirmed" or "archived". Dates are null and the count is 0 without Allocations.
public record ProjectResponse(
    string Type, Guid Id, Guid ClientId, string Name, string Status,
    DateOnly? StartDate, DateOnly? EndDate, int AllocationCount, DateTimeOffset DateModified, Guid? LastChangedBy);

// InPickers is false when the Client has Projects and all are Archived.
public record ClientResponse(
    string Type, Guid Id, string Name, bool InPickers, IReadOnlyList<ProjectResponse> Projects,
    DateTimeOffset DateModified, Guid? LastChangedBy);

public record ClientRequest(string? Name);

public record CreateProjectRequest(string? Name, string? Status);

public record UpdateProjectRequest(string? Name, Guid ClientId, string? Status);

// One Person of a Project with their Allocations, sorted by start date.
public record ProjectPersonResponse(string Type, Guid PersonId, string Name, IReadOnlyList<ScheduleAllocation> Allocations);
