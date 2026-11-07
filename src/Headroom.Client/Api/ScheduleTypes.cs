namespace Headroom.Client.Api;

// Shared API types of GET /api/schedule. Field names are a contract.
// The server computes Capacity and Utilisation. The client only renders them.

/// <summary>Type is a DayOffTypeName.</summary>
public record ScheduleDayOff(string Type, DateOnly Date, string Name);

/// <summary>Utilisation is Hours ÷ Capacity. It is null when Capacity is 0 (the grid shows a gap).</summary>
public record ScheduleDay(DateOnly Date, decimal Capacity, decimal Hours, decimal? Utilisation)
{
    public string Type => "ScheduleDay";
}

public record ScheduleAllocation(Guid Id, DateOnly StartDate, DateOnly EndDate, int Percentage)
{
    public string Type => "Allocation";
}

/// <summary>Status is "tentative", "confirmed" or "archived". The names are for display: the ids identify the records.</summary>
public record ScheduleProject(
    Guid ProjectId, Guid ClientId, string ClientName, string ProjectName, string Status, IReadOnlyList<ScheduleAllocation> Allocations)
{
    public string Type => "ScheduleProject";
}

/// <summary>StartPart and EndPart are "morning" or "afternoon".</summary>
public record ScheduleTimeOff(Guid Id, DateOnly StartDate, string StartPart, DateOnly EndDate, string EndPart)
{
    public string Type => "TimeOff";
}

/// <summary>Days has one entry per day from <see cref="ScheduleResponse.StartDate"/> to <see cref="ScheduleResponse.EndDate"/>.</summary>
public record SchedulePerson(
    string Type, Guid Id, string Name, string Status,
    IReadOnlyList<ScheduleDay> Days, IReadOnlyList<ScheduleProject> Projects, IReadOnlyList<ScheduleTimeOff> TimeOff);

/// <summary>StartDate and EndDate are inclusive. People are sorted by name.</summary>
public record ScheduleResponse(
    string Type, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<ScheduleDayOff> DaysOff, IReadOnlyList<SchedulePerson> People);
