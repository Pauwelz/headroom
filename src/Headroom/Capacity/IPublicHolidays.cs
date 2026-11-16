namespace Headroom.Capacity;

/// <summary>Tells whether a day is a public holiday. One implementation wraps the PublicHoliday package.</summary>
public interface IPublicHolidays
{
    bool IsPublicHoliday(DateOnly day);

    /// <summary>Every public holiday of a year with its name. Two holidays on one date share one name, joined with " / ".</summary>
    IReadOnlyDictionary<DateOnly, string> InYear(int year);
}
