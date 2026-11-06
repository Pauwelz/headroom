namespace Headroom.Client.Shell;

/// <summary>
/// The one source of the URL rules. The server (WebhookGuard, ConnectedAppEndpoints) and the forms use it.
/// Each method returns an error message, or null when the value is valid.
/// </summary>
public static class UrlRules
{
    private const int MaxLength = 2000;

    public const string NoRedirectUrl = "Enter at least one redirect URL.";

    public static string? WebhookUrl(string? value, bool development) => WebhookUrl(value, development, out _);

    /// <summary>An absolute https URL. Development also allows http.</summary>
    public static string? WebhookUrl(string? value, bool development, out Uri url) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out url!) && url.ToString().Length <= MaxLength
            && (url.Scheme == "https" || (development && url.Scheme == "http"))
            ? null
            : development ? "The URL must be an absolute http or https URL." : "The URL must be an absolute https URL.";

    public static string? RedirectUrl(string? value) => RedirectUrl(value, out _);

    /// <summary>An absolute https URL without a fragment.</summary>
    public static string? RedirectUrl(string? value, out Uri url) =>
        WebhookUrl(value, development: false, out url) is null && url.Fragment.Length == 0
            ? null
            : "The redirect URL must be an absolute https URL without a fragment.";

    /// <summary>One URL per line, at least one.</summary>
    public static string? RedirectUrls(string? text)
    {
        var lines = (text ?? "").Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? NoRedirectUrl : lines.Select(line => RedirectUrl(line)).FirstOrDefault(error => error is not null);
    }
}
