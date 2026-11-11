using System.Text.Json.Serialization;

namespace Headroom.Client.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompanyLoginKind { Entra, Other }

/// <summary>Shared API types of /api/settings/company-login. No response holds the client secret.
/// An empty ClientSecret keeps the stored one.</summary>
public record CompanyLoginRequest(CompanyLoginKind Kind, string Issuer, string ClientId, string? ClientSecret);

/// <summary>Kind is null when no Company login is set.</summary>
public record CompanyLoginResponse(CompanyLoginKind? Kind, string? Issuer, string? ClientId, bool HasSecret)
{
    public string Type => "CompanyLogin";
}

/// <summary>Where the test login starts. The browser goes there after Save.</summary>
public record CompanyLoginTestResponse(string TestUrl)
{
    public string Type => "CompanyLoginTest";
}

/// <summary>The result of a login with the Company login. It travels as the query value companyLogin.</summary>
public static class CompanyLoginOutcome
{
    public const string Saved = "saved";
    public const string Provider = "provider";
    public const string NoEmail = "no-email";
    public const string NotYou = "not-you";
    public const string Refused = "refused";

    public static string Message(string? code) => code switch
    {
        Provider => "The provider refused the login. Check the issuer, the client ID and the client secret.",
        NoEmail => "The provider returned no verified email address.",
        NotYou => "The test login does not match your Person in Headroom. Log in with the provider account that has your Headroom email address.",
        _ => "Headroom could not log you in with Company login. Use a Login code, or ask a Planner.",
    };
}
