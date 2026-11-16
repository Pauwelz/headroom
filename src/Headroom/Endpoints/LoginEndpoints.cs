using Headroom.Auth;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Static server-rendered login: one button that starts the login with Microsoft.
public static class LoginEndpoints
{
    public static void MapLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", ShowPage);
        app.MapGet(CompanyLogin.StartPath, Start);
    }

    private static async Task<IResult> ShowPage(HttpContext http, HeadroomDbContext db)
    {
        var returnUrl = SafeReturnUrl(http.Request.Query["ReturnUrl"]);
        if (http.User.Identity?.IsAuthenticated == true) return Results.Redirect(returnUrl);
        var outcome = http.Request.Query["companyLogin"].ToString();
        var company = await db.InstanceSettings.Select(s => s.CompanyName).FirstOrDefaultAsync();
        return ServerPages.Page(LoginPages.Login(company, outcome.Length == 0 ? null : CompanyLoginOutcome.Message(outcome), returnUrl));
    }

    // The handler loads the metadata of Entra ID here. An error ends in the provider outcome.
    private static async Task<IResult> Start(HttpContext http, ILogger<Program> log, string? returnUrl)
    {
        try
        {
            await http.ChallengeAsync(CompanyLogin.Scheme, new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) });
            return Results.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("Could not start the Company login: {ExceptionType}", ex.GetType().Name);
            return Results.Redirect($"/login?companyLogin={CompanyLoginOutcome.Provider}");
        }
    }

    internal static async Task SignInAsync(HttpContext http, Person person)
    {
        HeadroomMetrics.Login(success: true);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PersonPrincipal.Create(person));
    }

    // After login the Person returns to an authorize request of a Connected app. Nowhere else: no open redirect.
    internal static string SafeReturnUrl(string? url) =>
        url is not null && url.StartsWith("/connect/authorize?", StringComparison.Ordinal) && !url.Contains('\\') ? url : "/";
}
