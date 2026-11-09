namespace Headroom.Client.Schedule;

/// <summary>
/// Where a bar puts its controls, so they stay in the visible days when the bar extends past them.
/// The percentage and the menu anchor move in by <see cref="Before"/> days.
/// <see cref="DaysInView"/> is the number of visible days of the bar.
/// </summary>
public readonly record struct BarControls(int Before, int DaysInView)
{
    private const int Inset = 6;

    public string StartStyle => $"left:calc(var(--day) * {Before} + {Inset}px)";

    /// <param name="first">The first drawn day of the bar.</param>
    /// <param name="last">The last drawn day of the bar.</param>
    public static BarControls Of(DateOnly first, DateOnly last, DateOnly visibleFirst, DateOnly visibleLast)
    {
        // A bar outside the visible days keeps the percentage on its own days.
        var gaps = last.DayNumber - first.DayNumber;
        var before = Math.Clamp(visibleFirst.DayNumber - first.DayNumber, 0, gaps);
        var after = Math.Clamp(last.DayNumber - visibleLast.DayNumber, 0, gaps - before);
        return new(before, gaps + 1 - before - after);
    }
}
