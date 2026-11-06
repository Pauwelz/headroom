namespace Headroom.Auth;

// "Today" for every rule is the date in Europe/Brussels.
public static class BrusselsTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static DateOnly Today(TimeProvider time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);
}
