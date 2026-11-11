using System.Security.Cryptography;
using System.Text;
using Headroom.Data;
using Headroom.Email;
using Headroom.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// One-time email codes. First run and login both use it.
public class LoginCodeService(HeadroomDbContext db, IEmailSender mail, TimeProvider time)
{
    private const int MaxAttempts = 5;
    private const int MaxCodesPerWindow = 3;
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);
    public static string ValidityText => $"{Validity.TotalMinutes:0} minutes";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // Returns false, and sends nothing, when the email has had 3 codes in 15 minutes.
    public async Task<bool> IssueAsync(string email, string companyName, CancellationToken ct, SmtpOptions? server = null)
    {
        await SweepAsync(time.GetUtcNow(), ct);
        var now = time.GetUtcNow();
        if (await db.LoginCodes.CountAsync(c => c.Email == email, ct) >= MaxCodesPerWindow) return false;

        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        var row = new LoginCode { Email = email, CreatedAt = now, ExpiresAt = now + Validity };
        row.CodeHash = Hash(row.Id, code);
        db.LoginCodes.Add(row);
        await db.SaveChangesAsync(ct);

        try
        {
            await mail.SendLoginCodeAsync(email, companyName, code, ct, server);
        }
        catch
        {
            HeadroomMetrics.LoginCodeEmail(sent: false);
            throw;
        }
        HeadroomMetrics.LoginCodeEmail(sent: true);
        return true;
    }

    // Only the newest code of the email counts. A correct code loses its hash (single use).
    public async Task<bool> VerifyAsync(string email, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await SweepAsync(now, ct);
        var row = await db.LoginCodes.AsNoTracking()
            .Where(c => c.Email == email && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (row is null) return false;

        // One atomic statement, so parallel guesses cannot exceed the limit.
        var counted = await db.LoginCodes.Where(c => c.Id == row.Id && c.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (counted == 0 || !CryptographicOperations.FixedTimeEquals(row.CodeHash, Hash(row.Id, code))) return false;

        // The update returns 1 for one caller only, so one code signs in once.
        return await Scrub(db.LoginCodes.Where(c => c.Id == row.Id && c.CodeHash == row.CodeHash), ct) == 1;
    }

    // A used or expired code is gone at once. Its row stays without the hash until the
    // window ends, so a used code still counts toward the limit of 3 per 15 minutes.
    private async Task SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        await Scrub(db.LoginCodes.Where(c => c.ExpiresAt <= now), ct);
        await db.LoginCodes.Where(c => c.CreatedAt < now - Window).ExecuteDeleteAsync(ct);
    }

    private static Task<int> Scrub(IQueryable<LoginCode> codes, CancellationToken ct) =>
        codes.ExecuteUpdateAsync(s => s.SetProperty(c => c.CodeHash, Array.Empty<byte>()), ct);

    // shortcut: a salted SHA-256 of a 6-digit code is brute-forceable from a leaked database; the 10-minute life is the defence.
    private static byte[] Hash(Guid id, string code) => SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code}"));
}
