namespace Headroom.Client.Api;

// The string values of the API. Field values are a contract.

/// <summary>Project status values.</summary>
public static class ProjectStatusName
{
    public const string Tentative = "tentative";
    public const string Confirmed = "confirmed";
    public const string Archived = "archived";

    public static readonly IReadOnlyList<string> All = [Tentative, Confirmed, Archived];
}

/// <summary>The half of a day that starts or ends Time off.</summary>
public static class DayPartName
{
    public const string Morning = "morning";
    public const string Afternoon = "afternoon";
}
