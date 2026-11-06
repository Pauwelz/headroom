using PublicHoliday;

namespace Headroom.Capacity;

public sealed class BelgianPublicHolidays : IPublicHolidays
{
    public bool IsPublicHoliday(DateOnly day) => new BelgiumPublicHoliday().IsPublicHoliday(day.ToDateTime(TimeOnly.MinValue));

    public IReadOnlyDictionary<DateOnly, string> InYear(int year) =>
        new BelgiumPublicHoliday().PublicHolidayNames(year).ToDictionary(h => DateOnly.FromDateTime(h.Key), h => h.Value);
}
