namespace Headroom.Client.Utilisation;

/// <summary>The range of the Utilisation report: a calendar quarter plus the next.</summary>
public record UtilisationRange(DateOnly From)
{
    /// <summary>Last day, inclusive: the end of the next quarter.</summary>
    public DateOnly To => From.AddMonths(6).AddDays(-1);

    public static UtilisationRange Default(DateOnly today) => new(QuarterStart(today));

    /// <summary>A page moves one quarter.</summary>
    public UtilisationRange Page(int direction) => new(From.AddMonths(3 * direction));

    public string Label => $"{Quarter(From)} + {Quarter(From.AddMonths(3))}";

    private static DateOnly QuarterStart(DateOnly day) => new(day.Year, (day.Month - 1) / 3 * 3 + 1, 1);

    private static string Quarter(DateOnly day) => $"Q{(day.Month - 1) / 3 + 1} {day.Year}";
}
