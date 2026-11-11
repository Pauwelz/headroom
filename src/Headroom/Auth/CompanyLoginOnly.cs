using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// The one rule "is this email Company-login-only". The Login code, the Passkey login, the Passkey registration and the Passkey offer use it.
// A domain is the part after the last @. It matches exactly, without case. No subdomains, no wildcards.
public static class CompanyLoginOnly
{
    public static string? DomainOf(string? email) =>
        email?.LastIndexOf('@') is >= 0 and var at ? email[(at + 1)..].Trim().ToLowerInvariant() : null;

    // The domain of the email when Company login is the only login for it, otherwise null.
    public static string? Match(InstanceSettings? settings, string? email) =>
        settings is { CompanyLoginOnly: true } && DomainOf(email) is { } domain && settings.CompanyDomains.Contains(domain) ? domain : null;

    public static async Task<string?> MatchAsync(HeadroomDbContext db, string? email) =>
        Match(await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync(), email);

    // The domain in its stored form, or null when the text is not a domain name.
    public static string? CleanDomain(string? text) =>
        text?.Trim().TrimStart('@').ToLowerInvariant() is { Length: > 0 and <= 253 } domain && !domain.Any(c => c is '@' or '*' || char.IsWhiteSpace(c)) ? domain : null;

    // Deletes the Passkeys of the affected People, or of one Person. It does nothing while the switch is off.
    public static async Task DeletePasskeysAsync(HeadroomDbContext db, Guid? personId = null)
    {
        var settings = await db.InstanceSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is not { CompanyLoginOnly: true }) return;
        var owners = await db.Passkeys.Where(p => personId == null || p.PersonId == personId)
            .Join(db.People, p => p.PersonId, person => person.Id, (p, person) => new { p.Id, person.Email }).ToListAsync();
        var ids = owners.Where(o => Match(settings, o.Email) is not null).Select(o => o.Id).ToList();
        await db.Passkeys.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
    }
}
