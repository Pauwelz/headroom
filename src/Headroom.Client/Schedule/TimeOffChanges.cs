using Headroom.Client.Api;
using static Headroom.Client.Api.DayPartName;

namespace Headroom.Client.Schedule;

/// <summary>The requests behind the gestures on a Time off bar.
/// The server applies the rules (merge, end date). This only builds the new start and end.</summary>
public static class TimeOffChanges
{
    /// <summary>Click start day, click end day: whole days, in either order.</summary>
    public static TimeOffRequest Create(DateOnly a, DateOnly b) =>
        a <= b ? new(a, Morning, b, Afternoon) : new(b, Morning, a, Afternoon);

    /// <summary>Drag the bar: both days move, the half days stay.</summary>
    public static TimeOffRequest Move(ScheduleTimeOff t, int days) =>
        new(t.StartDate.AddDays(days), t.StartPart, t.EndDate.AddDays(days), t.EndPart);

    /// <summary>Drag the left edge to a day.</summary>
    public static TimeOffRequest ResizeStart(ScheduleTimeOff t, DateOnly day) => new(day, t.StartPart, t.EndDate, t.EndPart);

    /// <summary>Drag the right edge to a day.</summary>
    public static TimeOffRequest ResizeEnd(ScheduleTimeOff t, DateOnly day) => new(t.StartDate, t.StartPart, day, t.EndPart);

    public static bool IsSingleDay(ScheduleTimeOff t) => t.StartDate == t.EndDate;

    public static bool FirstDayIsHalf(ScheduleTimeOff t) => t.StartPart == Afternoon;

    public static bool LastDayIsHalf(ScheduleTimeOff t) => t.EndPart == Morning;

    /// <summary>Half day / Full day for the first day. A half first day is off in the afternoon only.</summary>
    public static TimeOffRequest FirstDay(ScheduleTimeOff t, bool half) =>
        new(t.StartDate, half ? Afternoon : Morning, t.EndDate, t.EndPart);

    /// <summary>Half day / Full day for the last day. A half last day is off in the morning only.</summary>
    public static TimeOffRequest LastDay(ScheduleTimeOff t, bool half) =>
        new(t.StartDate, t.StartPart, t.EndDate, half ? Morning : Afternoon);

    /// <summary>Morning / Afternoon / Full day on a single day.</summary>
    public static TimeOffRequest SingleDay(ScheduleTimeOff t, string part) => part switch
    {
        Morning => new(t.StartDate, Morning, t.EndDate, Morning),
        Afternoon => new(t.StartDate, Afternoon, t.EndDate, Afternoon),
        _ => new(t.StartDate, Morning, t.EndDate, Afternoon),
    };
}
