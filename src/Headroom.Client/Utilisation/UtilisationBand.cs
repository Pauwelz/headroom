using System.Globalization;
using Headroom.Client.Api;

namespace Headroom.Client.Utilisation;

/// <summary>Colour bands and number text of the heatmap.</summary>
public static class UtilisationBand
{
    /// <summary>"none", "low" (under 75%), "ideal" (75 to 90%), "high" (over 90 up to 100%) or "over" (above 100%).</summary>
    public static string Of(UtilisationCell cell) => cell.Utilisation switch
    {
        null => "none",
        > 1m => "over",
        > 0.9m => "high",
        >= 0.75m => "ideal",
        _ => "low",
    };

    /// <summary>"83%", or "—" when there is no Capacity.</summary>
    public static string Percent(decimal? value) => value is { } v ? $"{Math.Round(v * 100, MidpointRounding.AwayFromZero)}%" : "—";

    /// <summary>The Tentative part of Capacity, or null when there is none to show.</summary>
    public static string? TentativeNote(UtilisationCell cell) =>
        cell is { Tentative: > 0, Capacity: > 0 } ? $"incl. {Percent(cell.Tentative / cell.Capacity)} T" : null;

    /// <summary>Confirmed, Tentative and Archived hours together.</summary>
    public static decimal Allocated(UtilisationCell cell) => cell.Confirmed + cell.Tentative + cell.Archived;

    /// <summary>Capacity minus allocated hours. Negative when over-allocated.</summary>
    public static decimal Free(UtilisationCell cell) => cell.Capacity - Allocated(cell);

    /// <summary>Hours with at most one decimal: "8", "8.8".</summary>
    public static string Hours(decimal hours) => Math.Round(hours, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
