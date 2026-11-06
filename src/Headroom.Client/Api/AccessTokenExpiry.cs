namespace Headroom.Client.Api;

// The expiry choices of an Access token, and how a list shows an expiry. The server and the pages share this one source.
public static class AccessTokenExpiry
{
    public const string DefaultChoice = "90d";
    private static readonly TimeSpan SoonWindow = TimeSpan.FromDays(7);

    // A null Add means the token never expires.
    private static readonly (string Code, string Label, Func<DateTimeOffset, DateTimeOffset>? Add)[] Entries =
    [
        ("30d", "30 days", now => now.AddDays(30)),
        ("90d", "90 days", now => now.AddDays(90)),
        ("1y", "1 year", now => now.AddYears(1)),
        ("never", "Never", null),
    ];

    public static readonly IReadOnlyList<(string Code, string Label)> Choices = [.. Entries.Select(e => (e.Code, e.Label))];

    // "30 days, 90 days, 1 year or Never"
    public static string ChoiceText => string.Join(", ", Choices.SkipLast(1).Select(c => c.Label)) + " or " + Choices[^1].Label;

    // False for any code that is not a choice. True with a null moment means the token never expires.
    public static bool TryExpiresAt(string? choice, DateTimeOffset now, out DateTimeOffset? expiresAt)
    {
        var entry = Entries.FirstOrDefault(e => e.Code == choice);
        expiresAt = entry.Add?.Invoke(now);
        return entry.Code is not null;
    }

    public static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt <= now;

    public static bool ExpiresSoon(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt > now && expiresAt - now <= SoonWindow;

    public static string Text(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not { } at ? "Never"
        : IsExpired(at, now) ? "Expired"
        : DisplayDate.Format(DateOnly.FromDateTime(at.LocalDateTime));
}
