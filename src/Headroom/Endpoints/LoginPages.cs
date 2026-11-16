namespace Headroom.Endpoints;

// HTML of the login screen. The privacy notice is fixed. It opens on click.
public static class LoginPages
{
    private static readonly string[] Steps = ["Log in"];

    public static string Login(string? company, string? error, string returnUrl) =>
        ServerPages.Layout("Log in", error, Steps, 0, company, $$"""
            <form method="get" action="/login/company">
              {{ServerPages.Hidden("returnUrl", returnUrl)}}
              <button>Log in with Microsoft</button>
            </form>
            {{Privacy(company)}}
            """);

    private static string Privacy(string? company) => $"""
        <details class="privacy">
        <summary>Privacy notice</summary>
        <p>{(company is null ? "Your company" : ServerPages.E(company))} is the controller of your data. Headroom stores your name, your email address, your contracts, your allocations and your time off.</p>
        <p>The purpose is correct planning and planning history. The legal basis is legitimate interest (GDPR Art. 6(1)(f)).</p>
        <p>Headroom deletes you, with your contracts, allocations and time off, 12 months after your end date. A Planner can delete you earlier. Delete also blanks your name and email in stored Webhook Events. Headroom deletes each Event 30 days after it creates the Event. Headroom cannot recall an Event that a Webhook receiver already got. The database host of your company sets how long backups keep data.</p>
        </details>
        """;
}
