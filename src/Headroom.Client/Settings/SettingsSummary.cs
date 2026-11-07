using Headroom.Client.Api;

namespace Headroom.Client.Settings;

/// <summary>The one-line summary of each Settings area, and the counts behind the attention badges in the nav.</summary>
public static class SettingsSummary
{
    public static string DaysOff(IReadOnlyList<DayOffResponse> days, int year, DateOnly today)
    {
        var company = days.Where(d => d.Type == DayOffTypeName.CompanyDayOff).OrderBy(d => d.Date).ToList();
        if (company.Count == 0) return $"No Company days off in {year}";
        var text = $"{company.Count} {(company.Count == 1 ? "Company day off" : "Company days off")} in {year}";
        var next = company.FirstOrDefault(d => d.Date >= today);
        return next is null ? text : $"{text}. Next: {next.Name}, {DisplayDate.Format(next.Date)}";
    }

    public static string Tokens(IReadOnlyList<AccessTokenResponse> tokens) =>
        tokens.Count == 0 ? "No tokens" : $"{Count(tokens.Count, "token", "tokens")} of {People(tokens.Select(t => t.Owner))}";

    public static string Connections(IReadOnlyList<ConnectionResponse> connections) =>
        connections.Count == 0 ? "No Connections" : $"{Count(connections.Count, "Connection", "Connections")} of {People(connections.Select(c => c.Owner))}";

    /// <summary>The names of the Connected apps or the Webhooks.</summary>
    public static string Names(IEnumerable<string> names) => names.Any() ? string.Join(", ", names) : "None";

    public static int TokensExpiringSoon(IEnumerable<AccessTokenResponse> tokens, DateTimeOffset now) =>
        tokens.Count(t => AccessTokenExpiry.ExpiresSoon(t.ExpiresAt, now));

    // The accessible names of the nav badges, which show only the number.
    public static string TokensExpiringSoonLabel(int count) => count == 1 ? "1 token expires soon" : $"{count} tokens expire soon";

    public static string FailedEventsLabel(int count) => Count(count, "failed Event", "failed Events");

    private static string People(IEnumerable<string> owners) => Count(owners.Distinct().Count(), "Person", "People");

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
