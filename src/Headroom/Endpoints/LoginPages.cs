using Headroom.Auth;

namespace Headroom.Endpoints;

// HTML of the login screen. The privacy notice is fixed. It opens on click.
public static class LoginPages
{
    private static readonly string[] Steps = ["Email", "Code from email"];

    public static string Email(string token, string company, string? error, string email, string returnUrl) =>
        FirstRunPages.Layout("Log in", error, Steps, 0, company, $$"""
            <form method="post" action="/login">
              {{FirstRunPages.Hidden("__RequestVerificationToken", token)}}{{FirstRunPages.Hidden("ReturnUrl", returnUrl)}}
              <label>Email <input name="email" type="email" value="{{FirstRunPages.E(email)}}" maxlength="254" autocomplete="email" required autofocus></label>
              <button>Send code</button>
            </form>
            {{Privacy(company)}}
            """);

    public static string Code(string token, string company, string? error, string email, string returnUrl) =>
        FirstRunPages.Layout("Enter your code", error, Steps, 1, company, $$"""
            <p class="hint">If this email address is known, we sent a 6-digit code to it. The code works for {{LoginCodeService.ValidityText}}.</p>
            <form method="post" action="/login/verify">
              {{FirstRunPages.Hidden("__RequestVerificationToken", token)}}
              {{FirstRunPages.Hidden("email", email)}}{{FirstRunPages.Hidden("ReturnUrl", returnUrl)}}
              {{FirstRunPages.CodeField}}
              <button>Log in</button>
            </form>
            {{Privacy(company)}}
            """);

    private static string Privacy(string company) => $"""
        <details class="privacy">
        <summary>Privacy notice</summary>
        <p>{FirstRunPages.E(company)} is the controller of your data. Headroom stores your name, your email address, your contracts, your allocations and your time off.</p>
        <p>The purpose is correct planning and planning history. The legal basis is legitimate interest (GDPR Art. 6(1)(f)).</p>
        <p>Headroom erases your personal details 12 months after your end date. This includes your name and email in webhook events. Headroom deletes webhook events 30 days after it creates them, once they are delivered or given up. SQL backups keep data for up to 7 days.</p>
        </details>
        """;
}
