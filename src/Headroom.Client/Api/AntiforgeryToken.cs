namespace Headroom.Client.Api;

// Holds the antiforgery token that /api/me returns. AntiforgeryHandler sends it on every write.
public class AntiforgeryToken
{
    public const string HeaderName = "X-XSRF-TOKEN";

    public string? Value { get; set; }
}
