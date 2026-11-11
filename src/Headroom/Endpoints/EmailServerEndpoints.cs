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

// The email server in Settings. Planners only, and only with the login cookie: whoever controls the
// email server can read every Login code, so an Access token or a Connection never reaches it
// (the same rule as Connected apps and Access tokens).
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

    // The values of Smtp__* are the Operator's. The response only says that they apply.
    private static async Task<IResult> Get(HeadroomDbContext db, IOptions<SmtpOptions> environment)
    {
        var row = await db.InstanceSettings.AsNoTracking().FirstAsync();
        return Results.Ok(new EmailServerResponse(
            row.SmtpHost, row.SmtpPort ?? 587, row.SmtpUsername, row.SmtpFromAddress, row.SmtpPassword is not null,
            UsesEnvironment: row.SmtpHost is null && !string.IsNullOrEmpty(environment.Value.Host)));
    }

    private static async Task<IResult> Save(
        EmailServerRequest request, HttpContext http, HeadroomDbContext db, IEmailSender mail, EmailTestTimeout timeout,
        IDataProtectionProvider protection, ILogger<Program> log, CancellationToken ct)
    {
        var host = request.Host?.Trim() ?? "";
        var from = request.FromAddress?.Trim() ?? "";
        var username = request.Username?.Trim() ?? "";
        if (host.Length is 0 or > 253) return Problem(400, "Enter a host of 1 to 253 characters.");
        if (request.Port is < 1 or > 65535) return Problem(400, "Enter a port from 1 to 65535.");
        if (!FirstRunEndpoints.IsEmail(from)) return Problem(400, "Enter a valid From address.");
        if (username.Length > 254) return Problem(400, "Enter a user name of at most 254 characters.");
        if (request.Password is { Length: > 1000 }) return Problem(400, "Enter a password of at most 1000 characters.");

        // An empty password keeps the stored one, but only for the same server and user name.
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
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout.Value);
        try
        {
            await mail.SendTestAsync(server, planner.Email!, row.CompanyName ?? "", limit.Token);
        }
        catch (Exception ex)
        {
            // Log the exception type only. The message of a mail error can hold an email address.
            log.LogError("The test email failed: {ExceptionType}", ex.GetType().Name);
            return Problem(400, ConnectDetail);
        }

        EmailServer.Store(row, server, protection);
        row.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
