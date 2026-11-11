using System.Net.Mail;
using Headroom.Auth;
using Headroom.Data;
using Headroom.Client.Api;
using Headroom.Email;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Antiforgery;
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
        group.MapPost("/company-login", ShowCompanyLogin);
        group.MapPost("/company-login/test", StartCompanyLogin);
    }

    private static async Task<IResult> ShowForm(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, IOptions<SmtpOptions> smtp, IHostEnvironment env) =>
        await IsClosed(db) ? Results.NotFound() : FirstRunPages.Page(FirstRunPages.Form(Token(http, antiforgery), OutcomeError(http), "", "", "", AsksForServer(env, smtp)));

    // A test login with the Company login sends the browser back here with its outcome.
    private static string? OutcomeError(HttpContext http) =>
        http.Request.Query["companyLogin"].ToString() is { Length: > 0 } outcome ? CompanyLoginOutcome.Message(outcome) : null;

    private static string RedirectUri(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}{CompanyLogin.CallbackPath}";

    private static readonly FirstRunPages.CompanyLoginForm DefaultCompanyLoginForm = new("Entra", "", "");

    // The test login creates the first Planner from the token. It needs only the company name.
    private static async Task<IResult> ShowCompanyLogin(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, IFormCollection form)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var company = form["company"].ToString().Trim();
        var token = Token(http, antiforgery);
        return company.Length is 0 or > 200
            ? FirstRunPages.Page(FirstRunPages.Form(token, "Enter a company name of 1 to 200 characters.", company, "", ""), 400)
            : FirstRunPages.Page(FirstRunPages.CompanyLogin(token, null, company, DefaultCompanyLoginForm, RedirectUri(http)));
    }

    // Headroom stores nothing until the test login succeeds. The values wait in a cookie.
    private static async Task<IResult> StartCompanyLogin(HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, IFormCollection form)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var company = form["company"].ToString().Trim();
        if (company.Length is 0 or > 200) return Results.Redirect("/first-run");
        var request = new CompanyLoginRequest(
            Enum.TryParse<CompanyLoginKind>(form["kind"], out var kind) ? kind : (CompanyLoginKind)(-1),
            form["issuer"].ToString().Trim(), form["clientId"].ToString().Trim(), form["clientSecret"].ToString());
        var invalid = CompanyLoginEndpoints.Validate(request) ?? (request.ClientSecret!.Length == 0 ? "Enter the client secret." : null);
        if (invalid is not null)
            return FirstRunPages.Page(FirstRunPages.CompanyLogin(Token(http, antiforgery), invalid, company,
                new(request.Kind.ToString(), request.Issuer, request.ClientId), RedirectUri(http)), 400);
        new CompanyLogin.Pending(request.Kind, request.Issuer, request.ClientId, request.ClientSecret!, null, company).Write(http);
        return Results.Redirect(CompanyLogin.TestPath);
    }

    // Development logs the code. Smtp__Host is a fallback. Neither needs the Email server step.
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

        if (!HasIdentity(company, name, email))
            return FirstRunPages.Page(FirstRunPages.Form(token, IdentityDetail, company, name, email, asks), 400);
        if (asks) return FirstRunPages.Page(FirstRunPages.Server(token, null, company, name, email, DefaultServerForm));

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

    // The Email server step sends the login code through exactly these settings.
    // Headroom stores nothing until the code is right.
    private static async Task<IResult> SendServerCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes, IOptions<SmtpOptions> smtp, IHostEnvironment env,
        EmailTestTimeout timeout, IDataProtectionProvider protection, ILogger<Program> log, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db) || !AsksForServer(env, smtp)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);
        if (!HasIdentity(company, name, email))
            return FirstRunPages.Page(FirstRunPages.Form(token, IdentityDetail, company, name, email, true), 400);

        var request = new EmailServerRequest(
            form["host"].ToString().Trim(), int.TryParse(form["port"], out var port) ? port : 0,
            form["username"].ToString().Trim(), form["password"].ToString(), form["from"].ToString().Trim());
        var shown = new FirstRunPages.ServerForm(request.Host, request.Port.ToString(), request.Username ?? "", request.FromAddress);
        if (EmailServerEndpoints.Validate(request) is { } invalid)
            return FirstRunPages.Page(FirstRunPages.Server(token, invalid, company, name, email, shown), 400);
        var server = new SmtpOptions
        {
            Host = request.Host, Port = request.Port, Username = request.Username ?? "", Password = request.Password ?? "", FromAddress = request.FromAddress,
        };

        try
        {
            using var limit = EmailServerEndpoints.LinkedTimeout(timeout, ct);
            if (!await codes.IssueAsync(email, company, limit.Token, server))
                return FirstRunPages.Page(FirstRunPages.Server(token, "Too many codes for this email. Wait 15 minutes.", company, name, email, shown), 429);
        }
        catch (Exception ex)
        {
            // Log the exception type only. A mail error can hold an address. The page shows no reply.
            log.LogError("Could not send the first-run code: {ExceptionType}", ex.GetType().Name);
            return FirstRunPages.Page(FirstRunPages.Server(token, EmailServerEndpoints.ConnectDetail, company, name, email, shown), 400);
        }
        // The tested server travels as one protected value. The browser cannot change it.
        var protectedServer = TimeLimited(protection).Protect(JsonSerializer.Serialize(server), ServerLifetime);
        return FirstRunPages.Page(FirstRunPages.Code(token, null, company, name, email, protectedServer));
    }

    private static readonly TimeSpan ServerLifetime = TimeSpan.FromMinutes(30);

    private static readonly FirstRunPages.ServerForm DefaultServerForm = new("", EmailServerDefaults.Port.ToString(), "", "");

    private static ITimeLimitedDataProtector TimeLimited(IDataProtectionProvider protection) =>
        protection.CreateProtector("Headroom.FirstRunSmtp").ToTimeLimitedDataProtector();

    private static async Task<IResult> VerifyCode(
        HttpContext http, HeadroomDbContext db, IAntiforgery antiforgery, LoginCodeService codes, IOptions<SmtpOptions> smtp, IHostEnvironment env,
        IDataProtectionProvider protection, TimeProvider time, IFormCollection form, CancellationToken ct)
    {
        if (await IsClosed(db)) return Results.NotFound();
        var (company, name, email) = Read(form);
        var token = Token(http, antiforgery);

        if (!HasIdentity(company, name, email))
            return FirstRunPages.Page(FirstRunPages.Code(token, "The code is wrong or has expired.", company, name, email), 400);

        // Read the server before the code is used up. A bad value then does not waste the code.
        SmtpOptions? server = null;
        var protectedServer = form["server"].ToString();
        if (AsksForServer(env, smtp))
        {
            try { server = JsonSerializer.Deserialize<SmtpOptions>(TimeLimited(protection).Unprotect(protectedServer)); }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                return FirstRunPages.Page(FirstRunPages.Server(token, "The email server settings expired. Enter them again.", company, name, email, DefaultServerForm), 400);
            }
        }

        if (!await codes.VerifyAsync(email, form["code"].ToString().Trim(), ct))
            return FirstRunPages.Page(FirstRunPages.Code(token, "The code is wrong or has expired.", company, name, email, protectedServer), 400);

        var person = new Person { Name = name, Email = email, IsPlanner = true };
        var settings = new InstanceSettings { CompanyName = company };
        if (server is not null) EmailServer.Store(settings, server, protection);
        AddFirstPlanner(db, person, settings, BrusselsTime.Today(time));
        await db.SaveChangesAsync(ct);

        await LoginEndpoints.SignInAsync(http, person);
        LoginEndpoints.MarkPasskeyOffer(http);
        return Results.Redirect("/");
    }

    // Adds the first Planner, the Instance settings and the default Contract. The caller saves.
    // The first Planner is the "last changed by" of every record of the first run.
    internal static void AddFirstPlanner(HeadroomDbContext db, Person person, InstanceSettings settings, DateOnly today)
    {
        person.LastChangedBy = settings.LastChangedBy = person.Id;
        db.InstanceSettings.Add(settings);
        db.People.Add(person);
        db.Contracts.Add(new Contract
        {
            PersonId = person.Id, StartDate = today, LastChangedBy = person.Id,
            HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
        });
    }

    // shortcut: two Operators who finish at the same moment could create two Planners; the screen is for one Operator.
    private static Task<bool> IsClosed(HeadroomDbContext db) => db.InstanceSettings.AnyAsync();

    private static (string Company, string Name, string Email) Read(IFormCollection form) =>
        (form["company"].ToString().Trim(), form["name"].ToString().Trim(), form["email"].ToString().Trim());

    private const string IdentityDetail = "Fill in all fields with a valid email address.";

    private static bool HasIdentity(string company, string name, string email) =>
        company.Length is > 0 and <= 200 && name.Length is > 0 and <= 200 && IsEmail(email);

    internal static bool IsEmail(string email) =>
        email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    internal static string Token(HttpContext http, IAntiforgery antiforgery) =>
        antiforgery.GetAndStoreTokens(http).RequestToken!;
}
