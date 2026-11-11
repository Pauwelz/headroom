using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// How long the test email may take. Tests shorten it.
public record EmailTestTimeout(TimeSpan Value);

// The email server in Settings. Planners only, and only with the login cookie.
// Whoever controls the email server can read every Login code.
// So an Access token or a Connection never reaches it. Connected apps follow the same rule.
public static class EmailServerEndpoints
{
    public const string ConnectDetail = "Could not connect to the email server.";
    public const string PasswordAgainDetail = "Enter the password again for the new server.";

    public static void MapEmailServerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings/email-server").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        group.MapGet("/", Get);
        group.MapPut("/", Save);
    }

    // Settings and the first-run screen share these rules.
    // It returns the error text, or null when the values are fine.
    public static string? Validate(EmailServerRequest request) =>
        (request.Host?.Trim() ?? "").Length is 0 or > 253 ? "Enter a host of 1 to 253 characters."
        : request.Port is < 1 or > 65535 ? "Enter a port from 1 to 65535."
        : !FirstRunEndpoints.IsEmail(request.FromAddress?.Trim() ?? "") ? "Enter a valid From address."
        : (request.Username?.Trim() ?? "").Length > 254 ? "Enter a user name of at most 254 characters."
        : request.Password is { Length: > 1000 } ? "Enter a password of at most 1000 characters."
        : null;

    // A test email may take only so long. A timeout gives the same error as a refusal.
    public static CancellationTokenSource LinkedTimeout(EmailTestTimeout timeout, CancellationToken ct)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout.Value);
        return limit;
    }

    // The values of Smtp__* belong to the Operator. The response only says that they apply.
    private static async Task<IResult> Get(HeadroomDbContext db, IOptions<SmtpOptions> environment)
    {
        var row = await db.InstanceSettings.AsNoTracking().FirstAsync();
        return Results.Ok(new EmailServerResponse(
            row.SmtpHost, row.SmtpPort ?? EmailServerDefaults.Port, row.SmtpUsername, row.SmtpFromAddress, row.SmtpPassword is not null,
            UsesEnvironment: row.SmtpHost is null && !string.IsNullOrEmpty(environment.Value.Host)));
    }

    private static async Task<IResult> Save(
        EmailServerRequest request, HttpContext http, HeadroomDbContext db, IEmailSender mail, EmailTestTimeout timeout,
        IDataProtectionProvider protection, ILogger<Program> log, CancellationToken ct)
    {
        if (Validate(request) is { } invalid) return Problem(400, invalid);
        var (host, from, username) = (request.Host.Trim(), request.FromAddress.Trim(), request.Username?.Trim() ?? "");

        // An empty password keeps the stored one. This holds only for the same host, port and user name.
        var stored = await EmailServer.ReadStoredAsync(db, protection, ct);
        var password = request.Password ?? "";
        if (password.Length == 0 && stored is { Password.Length: > 0 })
        {
            var same = string.Equals(stored.Host, host, StringComparison.OrdinalIgnoreCase)
                && stored.Port == request.Port && stored.Username == username;
            if (!same) return Problem(400, PasswordAgainDetail);
            password = stored.Password;
        }
        var server = new SmtpOptions { Host = host, Port = request.Port, Username = username, Password = password, FromAddress = from };

        var row = await db.InstanceSettings.FirstAsync(ct);
        var planner = await db.People.AsNoTracking().FirstAsync(p => p.Id == http.User.PersonId(), ct);
        using var limit = LinkedTimeout(timeout, ct);
        try
        {
            await mail.SendTestAsync(server, planner.Email!, row.CompanyName ?? "", limit.Token);
        }
        catch (Exception ex)
        {
            // Log the exception type only. A mail error can hold an email address.
            log.LogError("The test email failed: {ExceptionType}", ex.GetType().Name);
            return Problem(400, ConnectDetail);
        }

        EmailServer.Store(row, server, protection);
        row.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
