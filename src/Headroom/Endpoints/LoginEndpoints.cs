using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Static server-rendered login.
public static class LoginEndpoints
{
    public static void MapLoginEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/login");
        group.MapGet("/", ShowForm);
        group.MapPost("/", SendCode);
        group.MapPost("/verify", VerifyCode);
        group.MapPost("/passkey/options", (HttpContext http, PasskeyCeremony ceremony) =>
            Results.Content(ceremony.BeginLogin(http, PasskeyPath), "application/json"));
        group.MapPost("/passkey", PasskeyLogin);
    }

    private const string PasskeyPath = "/login/passkey";

    private static async Task<IResult> ShowForm(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery)
    {
        var returnUrl = SafeReturnUrl(http.Request.Query["ReturnUrl"]);
        if (http.User.Identity?.IsAuthenticated == true) return Results.Redirect(returnUrl);
        var outcome = http.Request.Query["companyLogin"].ToString();
        return await Company(db) is { } company
            ? FirstRunPages.Page(LoginPages.Email(FirstRunEndpoints.Token(http, antiforgery), company, outcome.Length == 0 ? null : CompanyLoginOutcome.Message(outcome), "", returnUrl, await HasCompanyLogin(db)), allowLoginScript: true)
            : Results.Redirect("/first-run");
    }

    private static async Task<IResult> SendCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes,
        TimeProvider time, ILogger<Program> log, IFormCollection form, CancellationToken ct)
    {
        if (await Company(db) is not { } company) return Results.Redirect("/first-run");
        var email = form["email"].ToString().Trim();
        var returnUrl = SafeReturnUrl(form["ReturnUrl"]);
        var token = FirstRunEndpoints.Token(http, antiforgery);
        if (!FirstRunEndpoints.IsEmail(email))
            return FirstRunPages.Page(LoginPages.Email(token, company, "Enter a valid email address.", email, returnUrl, await HasCompanyLogin(db)), 400, allowLoginScript: true);

        // The reply depends only on the typed domain, so it never says whether the email is known.
        if (await CompanyLoginOnly.MatchAsync(db, email) is { } domain)
            return FirstRunPages.Page(LoginPages.Email(token, company, $"Use Company login for @{domain}", email, returnUrl, true), allowLoginScript: true);

        var person = await FindPerson(db, email, ct);
        if (person is not null && person.CanLogIn(BrusselsTime.Today(time)))
        {
            try
            {
                await codes.IssueAsync(person.Email!, company, ct); // false (rate limited) sends nothing and counts nothing
            }
            catch (Exception ex)
            {
                // Log the type only: a mail error message can hold the email address.
                log.LogError("Could not send the login code: {ExceptionType}", ex.GetType().Name);
            }
        }
        // Unknown email, ended Person, rate limit and mail error all give this same page.
        return FirstRunPages.Page(LoginPages.Code(token, company, null, email, returnUrl));
    }

    private static async Task<IResult> VerifyCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes,
        TimeProvider time, IFormCollection form, CancellationToken ct)
    {
        if (await Company(db) is not { } company) return Results.Redirect("/first-run");
        var email = form["email"].ToString().Trim();
        var returnUrl = SafeReturnUrl(form["ReturnUrl"]);
        var person = FirstRunEndpoints.IsEmail(email) ? await FindPerson(db, email, ct) : null;

        if (person is null || !person.CanLogIn(BrusselsTime.Today(time))
            || await CompanyLoginOnly.MatchAsync(db, person.Email) is not null
            || !await codes.VerifyAsync(person.Email!, form["code"].ToString().Trim(), ct))
        {
            HeadroomMetrics.Login(success: false);
            return FirstRunPages.Page(LoginPages.Code(
                FirstRunEndpoints.Token(http, antiforgery), company, "The code is wrong or has expired.", email, returnUrl), 400);
        }

        await SignInAsync(http, person);
        // Not for a Connected app consent screen.
        if (returnUrl == "/") MarkPasskeyOffer(http);
        return Results.Redirect(returnUrl);
    }

    private static async Task SignInAsync(HttpContext http, Person person)
    {
        HeadroomMetrics.Login(success: true);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PersonPrincipal.Create(person));
    }

    // One-time marker: the app offers a Passkey once, then deletes the cookie.
    internal static void MarkPasskeyOffer(HttpContext http) =>
        http.Response.Cookies.Append(Headroom.Client.Shell.PasskeyOffer.CookieName, "1", OfferCookie(TimeSpan.FromMinutes(5)));

    private static CookieOptions OfferCookie(TimeSpan? life) => new() { Secure = true, SameSite = SameSiteMode.Lax, MaxAge = life };

    // The body is the credential.toJSON() of the browser. The answer is JSON, because the login script follows the redirect itself.
    private static async Task<IResult> PasskeyLogin(
        HttpContext http, HeadroomDbContext db, PasskeyCeremony ceremony, TimeProvider time, string? returnUrl, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        var passkey = await ceremony.CompleteLoginAsync(http, await reader.ReadToEndAsync(ct), PasskeyPath, db);
        var person = passkey is null ? null : await db.People.FirstOrDefaultAsync(p => p.Id == passkey.PersonId, ct);
        if (passkey is null || person is null || !person.CanLogIn(BrusselsTime.Today(time))
            || await CompanyLoginOnly.MatchAsync(db, person.Email) is not null)
        {
            HeadroomMetrics.Login(success: false);
            return Results.BadRequest(new { error = "Headroom could not log you in with this Passkey. Use a Login code instead." });
        }

        passkey.LastUsedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await SignInAsync(http, person);
        // A stale marker must not offer a Passkey to a Person who just used one.
        http.Response.Cookies.Delete(Headroom.Client.Shell.PasskeyOffer.CookieName, OfferCookie(null));
        return Results.Ok(new { redirect = SafeReturnUrl(returnUrl) });
    }

    // After login the Person returns to an authorize request of a Connected app. Nowhere else: no open redirect.
    internal static string SafeReturnUrl(string? url) =>
        url is not null && url.StartsWith("/connect/authorize?", StringComparison.Ordinal) && !url.Contains('\\') ? url : "/";

    private static Task<Person?> FindPerson(HeadroomDbContext db, string email, CancellationToken ct) =>
        db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email, ct); // the column collation ignores case

    private static Task<bool> HasCompanyLogin(HeadroomDbContext db) =>
        db.InstanceSettings.AnyAsync(s => s.CompanyLoginKind != null);

    private static Task<string?> Company(HeadroomDbContext db) =>
        db.InstanceSettings.Select(s => s.CompanyName).FirstOrDefaultAsync();
}
