using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// Who Entra ID says logged in. Issuer holds the tenant id (tid) and Subject the object id (oid).
// Email is the preferred_username. Never use the email claim: a user can set it.
public record CompanyIdentity(string Issuer, string Subject, string? Email, string? Name)
{
    public static CompanyIdentity? FromClaims(Func<string, string?> claim)
    {
        static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return NonBlank(claim("tid")) is { } issuer && NonBlank(claim("oid")) is { } subject
            ? new(issuer, subject, NonBlank(claim("preferred_username")), NonBlank(claim("name"))) : null;
    }

    // The stored identifier decides. Only a Person without one matches on email, and then takes the identifier.
    // The caller saves the link after it checks that the Person may log in.
    public async Task<Person?> FindPersonAsync(HeadroomDbContext db)
    {
        var linked = await db.People.FirstOrDefaultAsync(p => p.CompanyLoginIssuer == Issuer && p.CompanyLoginSubject == Subject);
        if (linked is not null || Email is null) return linked;
        var person = await db.People.FirstOrDefaultAsync(p => p.Email == Email && p.CompanyLoginSubject == null);
        if (person is not null) (person.CompanyLoginIssuer, person.CompanyLoginSubject) = (Issuer, Subject);
        return person;
    }
}
