using Headroom.Client.Api;
using Headroom.Client.Schedule;

namespace Headroom.Client.TimeOff;

public record CalendarCell(DateOnly Date, bool InMonth, bool Weekend, ScheduleDayOff? DayOff);

/// <summary>Columns count from 0 (Monday). A half edge is at the middle of its column.</summary>
public record CalendarBar(string Name, int FirstColumn, int LastColumn, bool StartsHalf, bool EndsHalf, bool ContinuesBefore, bool ContinuesAfter, int Lane);

public record CalendarWeek(IReadOnlyList<CalendarCell> Cells, IReadOnlyList<CalendarBar> Bars);

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

    public static IReadOnlyList<CalendarWeek> Weeks(DateOnly month, ScheduleResponse data)
    {
        var (from, to) = VisibleRange(month);
        return Enumerable.Range(0, (to.DayNumber - from.DayNumber + 1) / 7)
            .Select(i => Week(from.AddDays(i * 7), month, data))
            .ToList();
    }

    private static CalendarWeek Week(DateOnly monday, DateOnly month, ScheduleResponse data)
    {
        var cells = Enumerable.Range(0, 7).Select(i => Cell(monday.AddDays(i), month, data)).ToList();
        return new CalendarWeek(cells, Bars(monday, data));
    }

    private static CalendarCell Cell(DateOnly date, DateOnly month, ScheduleResponse data) => new(
        date,
        date.Year == month.Year && date.Month == month.Month,
        ScheduleRange.IsWeekend(date),
        data.DaysOff.FirstOrDefault(d => d.Date == date));

    private static List<CalendarBar> Bars(DateOnly monday, ScheduleResponse data)
    {
        var sunday = monday.AddDays(6);
        var bars = data.People
            .SelectMany(p => p.TimeOff.Where(t => t.StartDate <= sunday && t.EndDate >= monday).Select(t => Bar(p.Name, t, monday)))
            .OrderBy(b => b.FirstColumn).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var laneEnds = new List<int>(); // last column used per lane
        for (var i = 0; i < bars.Count; i++)
        {
            var lane = laneEnds.FindIndex(end => end < bars[i].FirstColumn);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(0); }
            laneEnds[lane] = bars[i].LastColumn;
            bars[i] = bars[i] with { Lane = lane };
        }
        return bars;
    }

    private static CalendarBar Bar(string name, ScheduleTimeOff t, DateOnly monday)
    {
        var before = t.StartDate < monday;
        var after = t.EndDate > monday.AddDays(6);
        return new CalendarBar(
            name,
            before ? 0 : t.StartDate.DayNumber - monday.DayNumber,
            after ? 6 : t.EndDate.DayNumber - monday.DayNumber,
            !before && t.StartPart == DayPartName.Afternoon,
            !after && t.EndPart == DayPartName.Morning,
            before,
            after,
            0);
    }
}
