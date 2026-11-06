using System.Net;
using Headroom.Auth;
using Headroom.Client.Shell;
using MimeKit;
using MudBlazor;
using MudBlazor.Utilities;

namespace Headroom.Email;

// The login email: an HTML part in the Headroom look and a plain-text part with the same words.
public static class LoginCodeEmail
{
    public const string Subject = "Your Headroom login code";

    // The mark's amber. HeadroomTheme has no amber slot, so brand.css (--hr-amber) holds it; a test keeps both equal.
    public const string Amber = "#E9A21C";

    // The app palette (HeadroomTheme). Mail clients ignore CSS variables, so the values go inline.
    private static readonly Palette Light = HeadroomTheme.Theme.PaletteLight;
    private static readonly string Bg = Hex(Light.Background), Card = Hex(Light.Surface), Line = Hex(Light.LinesDefault),
        Petrol = Hex(Light.Primary), Muted = Hex(Light.TextSecondary), Ink = Hex(Light.TextPrimary);

    // Email-safe fallbacks (Segoe UI, Arial) instead of the app's system-ui.
    private const string Font = "Barlow, 'Segoe UI', Arial, sans-serif";
    private const string DisplayFont = "'Barlow Semi Condensed', Barlow, 'Segoe UI', Arial, sans-serif";

    public static MimeEntity Body(string companyName, string code) =>
        new BodyBuilder { HtmlBody = Html(companyName, code), TextBody = Text(companyName, code) }.ToMessageBody();

    public static string Text(string companyName, string code) => $"""
        Use this code to log in to Headroom.

        {code}

        The code works for {LoginCodeService.ValidityText}. Do not share it.

        {Footer(companyName)}
        """;

    // Table layout, inline styles and no images: mail clients drop SVG and external resources.
    public static string Html(string companyName, string code)
    {
        var company = WebUtility.HtmlEncode(companyName);
        return $$"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{{Subject}}</title></head>
            <body style="margin:0;padding:0;background:{{Bg}};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{{Bg}};"><tr><td align="center" style="padding:24px 12px;">
              <table role="presentation" width="480" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:480px;background:{{Card}};border:1px solid {{Line}};border-radius:8px;">
                <tr><td style="padding:28px 32px 8px 32px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                    <td aria-hidden="true" style="padding-right:12px;">
                      <table role="presentation" width="32" cellpadding="0" cellspacing="0" border="0">
                        <tr><td height="4" style="height:4px;line-height:4px;font-size:0;background:{{Amber}};border-radius:2px;">&nbsp;</td></tr>
                        <tr><td height="8" style="height:8px;line-height:8px;font-size:0;">&nbsp;</td></tr>
                        <tr><td align="center"><table role="presentation" width="20" cellpadding="0" cellspacing="0" border="0"><tr><td height="16" style="height:16px;line-height:16px;font-size:0;background:{{Petrol}};border-radius:2px;">&nbsp;</td></tr></table></td></tr>
                      </table>
                    </td>
                    <td style="font-family:{{DisplayFont}};font-size:28px;line-height:32px;font-weight:600;color:{{Petrol}};">Headroom</td>
                  </tr></table>
                  <div style="padding-top:6px;font-family:{{Font}};font-size:14px;line-height:20px;color:{{Muted}};">{{company}}</div>
                </td></tr>
                <tr><td style="padding:16px 32px 0 32px;font-family:{{Font}};font-size:16px;line-height:24px;color:{{Ink}};">Use this code to log in to Headroom.</td></tr>
                <tr><td style="padding:16px 32px;">
                  <div style="font-family:{{DisplayFont}};font-size:40px;line-height:48px;font-weight:600;letter-spacing:8px;color:{{Ink}};background:{{Bg}};border-radius:6px;padding:12px 16px;text-align:center;">{{code}}</div>
                </td></tr>
                <tr><td style="padding:0 32px 24px 32px;font-family:{{Font}};font-size:16px;line-height:24px;color:{{Ink}};">The code works for {{LoginCodeService.ValidityText}}. Do not share it.</td></tr>
                <tr><td style="padding:16px 32px 24px 32px;border-top:1px solid {{Line}};font-family:{{Font}};font-size:12px;line-height:18px;color:{{Muted}};">{{WebUtility.HtmlEncode(Footer(companyName))}}</td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string Footer(string companyName) =>
        $"Someone asked for a Headroom login code for this address at {companyName}. If that was not you, ignore this email.";

    private static string Hex(MudColor color) => color.ToString(MudColorOutputFormats.Hex);
}
