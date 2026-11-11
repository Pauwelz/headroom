using System.Net;
using System.Security.Cryptography;
using System.Text;
using Headroom.Auth;
using Headroom.Client.Shell;
using MudBlazor;
using MudBlazor.Utilities;

namespace Headroom.Endpoints;

// HTML of the first-run screen. Every user value goes through HtmlEncode.
public static class FirstRunPages
{
    private static readonly string[] Steps = ["Company and first Planner", "Code from email"];
    private static readonly string[] StepsWithServer = ["Company and first Planner", "Email server", "Code from email"];

    // The entered values of the Email server step. The password is never here: the Code page carries it protected.
    public record ServerForm(string Host, string Port, string Username, string FromAddress);

    // withServer: the Email server step follows (it is skipped in Development and when Smtp__Host is set).
    public static string Form(string token, string? error, string company, string name, string email, bool withServer = false) =>
        Layout("Set up Headroom", error, withServer ? StepsWithServer : Steps, 0, null, $$"""
            <p class="hint">The first Planner adds People, Clients and Projects. Headroom emails them a code to log in.</p>
            <form method="post" action="/first-run">
              {{Hidden("__RequestVerificationToken", token)}}
              <label>Company name <input name="company" value="{{E(company)}}" maxlength="200" required autofocus></label>
              <label>Name of the first Planner <input name="name" value="{{E(name)}}" maxlength="200" required></label>
              <label>Email of the first Planner <input name="email" type="email" value="{{E(email)}}" maxlength="254" required></label>
              <button>{{(withServer ? "Next" : "Send code")}}</button>
            </form>
            """);

    public static string Server(string token, string? error, string company, string name, string email, ServerForm server) =>
        Layout("Set the email server", error, StepsWithServer, 1, null, $$"""
            <p class="hint">Headroom sends the login code through this server. It sends a test email to <strong>{{E(email)}}</strong> first.</p>
            <form method="post" action="/first-run/server">
              {{Hidden("__RequestVerificationToken", token)}}
              {{Hidden("company", company)}}{{Hidden("name", name)}}{{Hidden("email", email)}}
              <label>Host <input name="host" value="{{E(server.Host)}}" maxlength="253" required autofocus></label>
              <label>Port <input name="port" type="number" min="1" max="65535" value="{{E(server.Port)}}" required></label>
              <label>User name <input name="username" value="{{E(server.Username)}}" maxlength="254" autocomplete="off"></label>
              <label>Password <input name="password" type="password" maxlength="1000" autocomplete="new-password"></label>
              <label>From address <input name="from" type="email" value="{{E(server.FromAddress)}}" maxlength="254" required></label>
              <button>Send code</button>
            </form>
            """);

    // server and protectedPassword: set when the Email server step ran. They travel as hidden fields; the password is protected.
    public static string Code(string token, string? error, string company, string name, string email, ServerForm? server = null, string protectedPassword = "") =>
        Layout("Enter your code", error, server is null ? Steps : StepsWithServer, server is null ? 1 : 2, null, $$"""
            <p class="hint">We sent a 6-digit code to <strong>{{E(email)}}</strong>. It works for {{LoginCodeService.ValidityText}}.</p>
            <form method="post" action="/first-run/verify">
              {{Hidden("__RequestVerificationToken", token)}}
              {{Hidden("company", company)}}{{Hidden("name", name)}}{{Hidden("email", email)}}
              {{(server is null ? "" : Hidden("host", server.Host) + Hidden("port", server.Port) + Hidden("username", server.Username)
                  + Hidden("from", server.FromAddress) + Hidden("password", protectedPassword))}}
              {{CodeField}}
              <button>Finish setup</button>
            </form>
            """);

    internal const string CodeField =
        """<label>Code <input class="code" name="code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required autofocus></label>""";

    // steps: the flow this page is part of; current: its 0-based place in it. company: shown in the panel when known.
    internal static string Layout(string title, string? error, string[] steps, int current, string? company, string body) => $$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{title}} - Headroom</title>
        <link rel="stylesheet" href="/brand.css"><link rel="stylesheet" href="/setup.css">
        <style>{{ColorTokens}}</style></head>
        <body>
        <aside class="panel">
          <div class="brand">
            <svg class="mark" viewBox="0 0 20 20" aria-hidden="true"><rect x="1" y="2" width="18" height="2.5" rx="1" class="ceiling"/><rect x="4" y="9" width="12" height="9" rx="1.5" class="load"/></svg>
            <span class="wordmark">Headroom</span>
            {{(company is null ? "" : $"<span class=\"company\">{E(company)}</span>")}}
          </div>
          <p class="lede">Plan who works on which Project, and see how much room each Person has left.</p>
          <ol class="steps">{{string.Concat(steps.Select((s, i) => i == current ? $"<li aria-current=\"step\">{s}</li>" : $"<li>{s}</li>"))}}</ol>
        </aside>
        <main>
          <p class="count">Step {{current + 1}} of {{steps.Length}}</p>
          <h1>{{title}}</h1>
          {{(error is null ? "" : $"<p class=\"error\" role=\"alert\">{E(error)}</p>")}}
          {{body}}
        </main>
        </body></html>
        """;

    // The app palette (HeadroomTheme) as CSS variables, so these pages use the same colours.
    private static readonly string ColorTokens = BuildColorTokens();

    private static string BuildColorTokens()
    {
        static string Vars(Palette p) => string.Concat(new (string Name, MudColor Color)[]
        {
            ("bg", p.Background), ("surface", p.Surface), ("text", p.TextPrimary), ("muted", p.TextSecondary),
            ("line", p.LinesInputs), ("primary", p.Primary), ("on-primary", p.PrimaryContrastText),
            ("error", p.Error), ("panel", p.AppbarBackground), ("on-panel", p.AppbarText),
        }.Select(v => $"--{v.Name}:{v.Color.ToString(MudColorOutputFormats.Hex)};"));
        var theme = HeadroomTheme.Theme;
        return $":root{{{Vars(theme.PaletteLight)}}}@media (prefers-color-scheme: dark){{:root{{{Vars(theme.PaletteDark)}}}}}";
    }

    // The colour tokens are the only inline style, so one hash covers every page.
    // Omit form-action, because the consent POST redirects to the app and the browser checks that redirect too.
    private static readonly string ContentSecurityPolicy =
        $"default-src 'none'; style-src 'self' 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ColorTokens)))}'; "
        + "font-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'";

    // Only the login page runs a script: the static /passkey-login.js, same origin, no inline script.
    private static readonly string LoginContentSecurityPolicy = ContentSecurityPolicy + "; script-src 'self'; connect-src 'self'";

    internal static IResult Page(string html, int status = 200, bool allowLoginScript = false) => new HtmlPage(html, status, allowLoginScript);

    // The policy stops framing (clickjacking) and any script or foreign resource on these pages.
    private sealed record HtmlPage(string Html, int Status, bool AllowLoginScript) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers.ContentSecurityPolicy = AllowLoginScript ? LoginContentSecurityPolicy : ContentSecurityPolicy;
            return Results.Content(Html, "text/html; charset=utf-8", statusCode: Status).ExecuteAsync(http);
        }
    }

    internal static string Hidden(string name, string value) => $"""<input type="hidden" name="{E(name)}" value="{E(value)}">""";

    internal static string E(string value) => WebUtility.HtmlEncode(value);
}
