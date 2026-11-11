using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Email;

// Which email server serves a send, and how Headroom keeps the one a Planner sets.
public static class EmailServer
{
    public enum Source { Database, Environment, Console }

    // The order is: stored settings, then Smtp__*, then the console in Development. The function is pure.
    public static Source Resolve(bool hasStored, string? environmentHost, bool isDevelopment) =>
        hasStored ? Source.Database
        : !string.IsNullOrEmpty(environmentHost) ? Source.Environment
        : isDevelopment ? Source.Console
        : throw new InvalidOperationException("No email server is set.");

    private const string Purpose = "Headroom.SmtpPassword";

    // Null when no settings are stored.
    public static async Task<SmtpOptions?> ReadStoredAsync(HeadroomDbContext db, IDataProtectionProvider protection, CancellationToken ct)
    {
        var row = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return row?.SmtpHost is null ? null : new SmtpOptions
        {
            Host = row.SmtpHost,
            Port = row.SmtpPort ?? EmailServerDefaults.Port,
            Username = row.SmtpUsername ?? "",
            Password = row.SmtpPassword is null ? "" : protection.CreateProtector(Purpose).Unprotect(row.SmtpPassword),
            FromAddress = row.SmtpFromAddress ?? "",
        };
    }

    public static void Store(InstanceSettings row, SmtpOptions server, IDataProtectionProvider protection)
    {
        row.SmtpHost = server.Host;
        row.SmtpPort = server.Port;
        row.SmtpUsername = server.Username.Length == 0 ? null : server.Username;
        row.SmtpPassword = server.Password.Length == 0 ? null : protection.CreateProtector(Purpose).Protect(server.Password);
        row.SmtpFromAddress = server.FromAddress;
    }
}
