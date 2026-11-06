using System.Globalization;

namespace Headroom.Client.Api;

/// <summary>
/// The one date format users see: Belgian dd/mm/yyyy. The server and the client both use it.
/// API fields keep YYYY-MM-DD with no time zone: never use this class for them.
/// </summary>
public static class DisplayDate
{
    public const string Pattern = "dd/MM/yyyy";

    public static string Format(DateOnly date) => date.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>A month heading: "November 2026", or "Nov" when little room is left.</summary>
    public static string Month(DateOnly date, bool shortName) =>
        date.ToString(shortName ? "MMM" : "MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Format(DateOnly? date, string whenNull = "") => date is { } d ? Format(d) : whenNull;

    /// <summary>A timestamp in the time zone of the browser, for "last changed at".</summary>
    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString(Pattern + " HH:mm", CultureInfo.InvariantCulture);
}
