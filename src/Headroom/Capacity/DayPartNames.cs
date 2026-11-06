using Headroom.Client.Api;

namespace Headroom.Capacity;

/// <summary>The one mapping between <see cref="DayPart"/> and its API string.</summary>
public static class DayPartNames
{
    public static string ToApi(DayPart part) => part == DayPart.Morning ? DayPartName.Morning : DayPartName.Afternoon;

    public static bool TryParse(string? text, out DayPart part)
    {
        part = DayPart.Morning;
        if (string.Equals(text, DayPartName.Morning, StringComparison.OrdinalIgnoreCase)) return true;
        part = DayPart.Afternoon;
        return string.Equals(text, DayPartName.Afternoon, StringComparison.OrdinalIgnoreCase);
    }
}
