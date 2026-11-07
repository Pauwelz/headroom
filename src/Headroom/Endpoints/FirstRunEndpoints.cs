using System.Net.Mail;
using Headroom.Auth;
using Headroom.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Endpoints;

// Static server-rendered pages. They stay open until the Instance settings row exists.
public static class FirstRunEndpoints
{
    public static void MapFirstRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/first-run");
        group.MapGet("/", ShowForm);
        group.MapPost("/", SendCode);
        group.MapPost("/verify", VerifyCode);
    }

    private static async Task<IResult> ShowForm(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery) =>
        await IsClosed(db) ? Results.NotFound() : FirstRunPages.Page(FirstRunPages.Form(Token(http, antiforgery), null, "", "", ""));

    private static async Task<IResult> SendCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes,
        ILogger<Program> log, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);

        if (company.Length is 0 or > 200 || name.Length is 0 or > 200 || !IsEmail(email))
            return FirstRunPages.Page(FirstRunPages.Form(token, "Fill in all fields with a valid email address.", company, name, email), 400);

        try
        {
            if (!await codes.IssueAsync(email, company, ct))
                return FirstRunPages.Page(FirstRunPages.Form(token, "Too many codes for this email. Wait 15 minutes.", company, name, email), 429);
        }
        catch (Exception ex)
        {
            // Log the exception only. The message of a mail error can hold the email address.
            log.LogError("Could not send the first-run code: {ExceptionType}", ex.GetType().Name);
            return FirstRunPages.Page(FirstRunPages.Form(token, "The email could not be sent. Check the SMTP settings.", company, name, email), 503);
        }
        return FirstRunPages.Page(FirstRunPages.Code(token, null, company, name, email));
    }

    private static async Task<IResult> VerifyCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes,
        TimeProvider time, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);

        if (company.Length is 0 or > 200 || name.Length is 0 or > 200 || !IsEmail(email)
            || !await codes.VerifyAsync(email, form["code"].ToString().Trim(), ct))
            return FirstRunPages.Page(FirstRunPages.Code(token, "The code is wrong or has expired.", company, name, email), 400);

        var today = BrusselsTime.Today(time);
        var person = new Person { Name = name, Email = email, IsPlanner = true };
        // The first Planner is the "last changed by" of every record of the first run.
        person.LastChangedBy = person.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = company, LastChangedBy = person.Id });
        db.People.Add(person);
        db.Contracts.Add(new Contract
        {
            PersonId = person.Id, StartDate = today, LastChangedBy = person.Id,
            HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
        });
        await db.SaveChangesAsync(ct);

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PersonPrincipal.Create(person));
        LoginEndpoints.MarkPasskeyOffer(http);
        return Results.Redirect("/");
    }

    // shortcut: two Operators who finish at the same moment could create two Planners; the screen is for one Operator.
    private static Task<bool> IsClosed(HeadroomDbContext db) => db.InstanceSettings.AnyAsync();

    private static (string Company, string Name, string Email) Read(IFormCollection form) =>
        (form["company"].ToString().Trim(), form["name"].ToString().Trim(), form["email"].ToString().Trim());

    internal static bool IsEmail(string email) =>
        email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    internal static string Token(HttpContext http, IAntiforgery antiforgery) =>
        antiforgery.GetAndStoreTokens(http).RequestToken!;
}
