using Headroom.Client.Api;

namespace Headroom.Client.Settings;

// The notes on the Settings page. Plain logic, no UI.
public static class DayOffNotes
{
    /// <summary>Warning for a Company day off on this date. Null when there is none.
    /// shortcut: the public holidays come from the year on screen, so a date in another year gets no holiday note.</summary>
    public static string? ForCompanyDay(DateOnly date, IEnumerable<DayOffResponse> shownDays)
    {
        if (shownDays.Any(d => d.Type == DayOffTypeName.PublicHoliday && d.Date == date)) return "Already a public holiday: no effect.";
        return IsWeekend(date) ? "Weekend: affects only weekend Contract hours." : null;
    }

    /// <summary>A public holiday on a weekend offers a replacement day. Null otherwise.</summary>
    public static string? ForPublicHoliday(DateOnly date) =>
        IsWeekend(date) ? $"On a {date.DayOfWeek}: add a replacement day?" : null;

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
