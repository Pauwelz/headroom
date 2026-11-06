namespace Headroom.Client.Schedule;

/// <summary>The visible days of the Schedule. Weeks start on Monday.</summary>
public record ScheduleRange(DateOnly From, DateOnly To)
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>A preset keeps the first day of the range.</summary>
    public record Preset(string Label, Func<DateOnly, DateOnly> LastDay)
    {
        public ScheduleRange Apply(DateOnly from) => new(from, LastDay(from));
    }

    public static readonly Preset TwoWeeks = new("2 weeks", from => from.AddDays(13));
    public static readonly Preset SixWeeks = new("6 weeks", from => from.AddDays(41));
    // Calendar months: 89 to 92 days. It is also the longest range the picker accepts.
    public static readonly Preset ThreeMonths = new("3 months", from => from.AddMonths(3).AddDays(-1));

    public static readonly IReadOnlyList<Preset> Presets = [TwoWeeks, SixWeeks, ThreeMonths];

    /// <summary>The number of days that fit the width. It does not change while you scroll.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>6 weeks, from the Monday of this week.</summary>
    public static ScheduleRange Default(DateOnly today) => SixWeeks.Apply(MondayOf(today));

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static DateOnly TodayInBrussels(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Brussels).DateTime);

    /// <summary>
    /// The error for a range from the picker or a preset, or null. Moving does not check this:
    /// a range keeps its days when it moves, whatever month it moves to.
    /// </summary>
    public static string? Validate(DateOnly from, DateOnly to) =>
        to < from ? "The end date must be on or after the start date."
        : to > ThreeMonths.LastDay(from) ? "Pick at most 3 months."
        : null;

    /// <summary>The range with the same number of days, starting on another day.</summary>
    public ScheduleRange StartingAt(DateOnly from) => new(from, from.AddDays(Days - 1));

    /// <summary>One range back (-1) or forward (1), by its own number of days.</summary>
    public ScheduleRange Move(int direction) => StartingAt(From.AddDays(direction * Days));

    private const int LeadDays = 21;

    /// <summary>The loaded days: the range with 3 weeks on each side. The server allows 140 days.</summary>
    public int WindowDays => Days + 2 * LeadDays;

    // The window moves when fewer loaded days remain on a side.
    private const int MarginDays = 7;

    /// <summary>
    /// The first loaded day. The current window stays while at least a week of loaded days remains on both sides
    /// of the visible days. Otherwise, the window moves to the Monday 3 weeks before From.
    /// </summary>
    public DateOnly WindowFrom(DateOnly? current) =>
        current is { } c && From.DayNumber - c.DayNumber >= MarginDays && c.DayNumber + WindowDays - 1 - To.DayNumber >= MarginDays
            ? c
            : MondayOf(From).AddDays(-LeadDays);
}
