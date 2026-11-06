using Headroom.Auth;
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
    }

    private static async Task<IResult> ShowForm(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery)
    {
        var returnUrl = SafeReturnUrl(http.Request.Query["ReturnUrl"]);
        if (http.User.Identity?.IsAuthenticated == true) return Results.Redirect(returnUrl);
        return await Company(db) is { } company
            ? FirstRunPages.Page(LoginPages.Email(FirstRunEndpoints.Token(http, antiforgery), company, null, "", returnUrl))
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
            return FirstRunPages.Page(LoginPages.Email(token, company, "Enter a valid email address.", email, returnUrl), 400);

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
            || !await codes.VerifyAsync(person.Email!, form["code"].ToString().Trim(), ct))
        {
            HeadroomMetrics.Login(success: false);
            return FirstRunPages.Page(LoginPages.Code(
                FirstRunEndpoints.Token(http, antiforgery), company, "The code is wrong or has expired.", email, returnUrl), 400);
        }

        HeadroomMetrics.Login(success: true);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PersonPrincipal.Create(person));
        return Results.Redirect(returnUrl);
    }

    // After login the Person returns to an authorize request of a Connected app. Nowhere else: no open redirect.
    private static string SafeReturnUrl(string? url) =>
        url is not null && url.StartsWith("/connect/authorize?", StringComparison.Ordinal) && !url.Contains('\\') ? url : "/";

    private static Task<Person?> FindPerson(HeadroomDbContext db, string email, CancellationToken ct) =>
        db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email, ct); // column collation ignores case

    private static Task<string?> Company(HeadroomDbContext db) =>
        db.InstanceSettings.Select(s => s.CompanyName).FirstOrDefaultAsync();
}
