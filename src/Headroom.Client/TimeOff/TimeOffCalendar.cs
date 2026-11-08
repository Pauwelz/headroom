using Headroom.Client.Api;
using Headroom.Client.Schedule;

namespace Headroom.Client.TimeOff;

/// <summary>Part is "morning" or "afternoon" on a half day. It is null on a full day.</summary>
public record CalendarEntry(string Name, string? Part);

public record CalendarCell(DateOnly Date, bool InMonth, bool Weekend, ScheduleDayOff? DayOff, IReadOnlyList<CalendarEntry> Entries);

// The logic of the Time off calendar page. The page only renders the weeks.
public static class TimeOffCalendar
{
    /// <summary>The Monday on or before the 1st, to the Sunday on or after the last day.</summary>
    public static (DateOnly From, DateOnly To) VisibleRange(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return (ScheduleRange.MondayOf(first), ScheduleRange.MondayOf(last).AddDays(6));
    }

    public static IReadOnlyList<IReadOnlyList<CalendarCell>> Weeks(DateOnly month, ScheduleResponse data)
    {
        var (from, to) = VisibleRange(month);
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i));
        return days.Select(d => Cell(d, month, data)).Chunk(7).Select(w => (IReadOnlyList<CalendarCell>)w).ToList();
    }

    private static CalendarCell Cell(DateOnly date, DateOnly month, ScheduleResponse data) => new(
        date,
        date.Year == month.Year && date.Month == month.Month,
        ScheduleRange.IsWeekend(date),
        data.DaysOff.FirstOrDefault(d => d.Date == date),
        data.People
            .SelectMany(p => p.TimeOff.Where(t => t.StartDate <= date && date <= t.EndDate).Select(t => new CalendarEntry(p.Name, PartOn(t, date))))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());

    // A one-day Time off is a half day when both parts are the same.
    private static string? PartOn(ScheduleTimeOff t, DateOnly date)
    {
        if (t.StartDate == t.EndDate) return t.StartPart == t.EndPart ? t.StartPart : null;
        if (date == t.StartDate && t.StartPart == DayPartName.Afternoon) return DayPartName.Afternoon;
        if (date == t.EndDate && t.EndPart == DayPartName.Morning) return DayPartName.Morning;
        return null;
    }
}
