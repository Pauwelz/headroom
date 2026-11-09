namespace Headroom.Client.Schedule;

/// <summary>
/// Where a bar puts its controls, so they stay in the visible days when the bar extends past them.
/// The percentage moves in by <see cref="Before"/> days, the menu by <see cref="After"/> days.
/// <see cref="Visible"/> is the number of days between them, the bar's own days included.
/// </summary>
public readonly record struct BarControls(int Before, int After, int Visible)
{
    /// <param name="first">The first drawn day of the bar.</param>
    /// <param name="last">The last drawn day of the bar.</param>
    public static BarControls Of(DateOnly first, DateOnly last, DateOnly visibleFirst, DateOnly visibleLast)
    {
        // A bar outside the visible days keeps both controls on its own days.
        var gaps = last.DayNumber - first.DayNumber;
        var before = Math.Clamp(visibleFirst.DayNumber - first.DayNumber, 0, gaps);
        var after = Math.Clamp(last.DayNumber - visibleLast.DayNumber, 0, gaps - before);
        return new(before, after, gaps + 1 - before - after);
    }
}
