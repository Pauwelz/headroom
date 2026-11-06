namespace Headroom.Client.Api;

/// <summary>Body of a Time off write. StartPart and EndPart are "morning" or "afternoon". The response is a <see cref="ScheduleTimeOff"/>.</summary>
public record TimeOffRequest(DateOnly StartDate, string StartPart, DateOnly EndDate, string EndPart);
