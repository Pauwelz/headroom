using System.Net.Mail;
using Headroom.Auth;
using Headroom.Data;
using Headroom.Client.Api;
using Headroom.Email;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
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
        group.MapPost("/server", SendServerCode);
        group.MapPost("/verify", VerifyCode);
    }

    private static async Task<IResult> ShowForm(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, IOptions<SmtpOptions> smtp, IHostEnvironment env) =>
        await IsClosed(db) ? Results.NotFound() : FirstRunPages.Page(FirstRunPages.Form(Token(http, antiforgery), null, "", "", "", AsksForServer(env, smtp)));

    // Development logs the code and Smtp__Host is a fallback, so neither needs the Email server step (and nothing is stored).
    private static bool AsksForServer(IHostEnvironment env, IOptions<SmtpOptions> smtp) =>
        !env.IsDevelopment() && string.IsNullOrEmpty(smtp.Value.Host);

    private static async Task<IResult> SendCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes, IOptions<SmtpOptions> smtp, IHostEnvironment env,
        ILogger<Program> log, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);
        var asks = AsksForServer(env, smtp);

        if (company.Length is 0 or > 200 || name.Length is 0 or > 200 || !IsEmail(email))
            return FirstRunPages.Page(FirstRunPages.Form(token, "Fill in all fields with a valid email address.", company, name, email, asks), 400);
        if (asks) return FirstRunPages.Page(FirstRunPages.Server(token, null, company, name, email, new("", "587", "", "")));

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

    // The Email server step: the login code goes through exactly these settings. Nothing is stored until the code is right.
    private static async Task<IResult> SendServerCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes, IOptions<SmtpOptions> smtp, IHostEnvironment env,
        EmailTestTimeout timeout, IDataProtectionProvider protection, ILogger<Program> log, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db) || !AsksForServer(env, smtp)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);
        if (company.Length is 0 or > 200 || name.Length is 0 or > 200 || !IsEmail(email))
            return FirstRunPages.Page(FirstRunPages.Form(token, "Fill in all fields with a valid email address.", company, name, email, true), 400);

        var request = ReadServer(form, form["password"].ToString());
        var shown = ShownOf(request);
        if (EmailServerEndpoints.Validate(request) is { } invalid)
            return FirstRunPages.Page(FirstRunPages.Server(token, invalid, company, name, email, shown), 400);

        try
        {
            using var limit = EmailServerEndpoints.Limit(timeout, ct);
            if (!await codes.IssueAsync(email, company, limit.Token, ServerOf(request)))
                return FirstRunPages.Page(FirstRunPages.Server(token, "Too many codes for this email. Wait 15 minutes.", company, name, email, shown), 429);
        }
        catch (Exception ex)
        {
            // Log the exception type only. The message of a mail error can hold an address, and the page shows no reply.
            log.LogError("Could not send the first-run code: {ExceptionType}", ex.GetType().Name);
            return FirstRunPages.Page(FirstRunPages.Server(token, EmailServerEndpoints.ConnectDetail, company, name, email, shown), 400);
        }
        // The password is not in a hidden field in plain text: it is protected and expires.
        var protectedPassword = request.Password is { Length: > 0 } ? TimeLimited(protection).Protect(request.Password, PasswordLifetime) : "";
        return FirstRunPages.Page(FirstRunPages.Code(token, null, company, name, email, shown, protectedPassword));
    }

    private static readonly TimeSpan PasswordLifetime = TimeSpan.FromMinutes(30);

    private static ITimeLimitedDataProtector TimeLimited(IDataProtectionProvider protection) =>
        protection.CreateProtector("Headroom.FirstRunSmtp").ToTimeLimitedDataProtector();

    private static EmailServerRequest ReadServer(IFormCollection form, string password) =>
        new(form["host"].ToString().Trim(), int.TryParse(form["port"], out var port) ? port : 0, form["username"].ToString().Trim(), password, form["from"].ToString().Trim());

    private static FirstRunPages.ServerForm ShownOf(EmailServerRequest r) => new(r.Host, r.Port.ToString(), r.Username ?? "", r.FromAddress);

    private static SmtpOptions ServerOf(EmailServerRequest r) =>
        new() { Host = r.Host, Port = r.Port, Username = r.Username ?? "", Password = r.Password ?? "", FromAddress = r.FromAddress };

    private static async Task<IResult> VerifyCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes, IOptions<SmtpOptions> smtp, IHostEnvironment env,
        IDataProtectionProvider protection, TimeProvider time, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);

        if (company.Length is 0 or > 200 || name.Length is 0 or > 200 || !IsEmail(email))
            return FirstRunPages.Page(FirstRunPages.Code(token, "The code is wrong or has expired.", company, name, email), 400);

        // Read the settings before the code is used up, so that a bad value does not waste the code.
        SmtpOptions? server = null;
        FirstRunPages.ServerForm? shown = null;
        var protectedPassword = form["password"].ToString();
        if (AsksForServer(env, smtp))
        {
            var request = ReadServer(form, "");
            shown = ShownOf(request);
            try { request = request with { Password = protectedPassword.Length == 0 ? "" : TimeLimited(protection).Unprotect(protectedPassword) }; }
            catch (CryptographicException)
            {
                return FirstRunPages.Page(FirstRunPages.Server(token, "The email server settings expired. Enter them again.", company, name, email, shown), 400);
            }
            if (EmailServerEndpoints.Validate(request) is { } invalid)
                return FirstRunPages.Page(FirstRunPages.Server(token, invalid, company, name, email, shown), 400);
            server = ServerOf(request);
        }

        if (!await codes.VerifyAsync(email, form["code"].ToString().Trim(), ct))
            return FirstRunPages.Page(FirstRunPages.Code(token, "The code is wrong or has expired.", company, name, email, shown, protectedPassword), 400);

        var today = BrusselsTime.Today(time);
        var person = new Person { Name = name, Email = email, IsPlanner = true };
        // The first Planner is the "last changed by" of every record of the first run.
        person.LastChangedBy = person.Id;
        var settings = new InstanceSettings { CompanyName = company, LastChangedBy = person.Id };
        if (server is not null) EmailServer.Store(settings, server, protection);
        db.InstanceSettings.Add(settings);
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
