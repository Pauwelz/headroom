using Headroom.Client.Api;

namespace Headroom.Client.Schedule;

/// <summary>The new dates behind the gestures on an Allocation bar.
/// The server applies the rules (latest change wins, end date). This only builds the new dates.</summary>
public static class AllocationChanges
{
    /// <summary>The day the second part starts on, near the wanted day. Null for a one-day bar, which cannot split.</summary>
    public static DateOnly? SplitDay(ScheduleAllocation a, DateOnly? wanted)
    {
        if (a.EndDate == a.StartDate) return null;
        var middle = a.StartDate.AddDays((a.EndDate.DayNumber - a.StartDate.DayNumber + 1) / 2);
        var day = wanted ?? middle;
        return day <= a.StartDate ? a.StartDate.AddDays(1) : day > a.EndDate ? a.EndDate : day;
    }
}
