using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// Who the provider says logged in. For Entra, Issuer holds the tenant id and Subject the object id.
// Email is what the first login matches on. It is null when the provider gives no email Headroom may trust.
public record CompanyIdentity(string Issuer, string Subject, string? Email, string? Name)
{
    private static readonly string[] MultiTenant = ["common", "organizations", "consumers"];

    // The claim names follow the kind. Entra never uses the email claim: a user can set it.
    public static CompanyIdentity? FromClaims(CompanyLoginKind kind, Func<string, string?> claim)
    {
        static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var (issuer, subject) = kind == CompanyLoginKind.Entra ? (NonBlank(claim("tid")), NonBlank(claim("oid"))) : (NonBlank(claim("iss")), NonBlank(claim("sub")));
        if (issuer is null || subject is null) return null;
        var email = kind == CompanyLoginKind.Entra ? NonBlank(claim("preferred_username"))
            : string.Equals(claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase) ? NonBlank(claim("email")) : null;
        return new(issuer, subject, email, NonBlank(claim("name")));
    }

    // The error text, or null when the issuer is fine. Entra needs one tenant. Plain http is only for a local provider.
    public static string? ValidateIssuer(CompanyLoginKind kind, string? issuer)
    {
        if (!Uri.TryCreate(issuer?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            return "Enter the issuer as an https URL.";
        if (kind == CompanyLoginKind.Entra && (uri.Scheme != Uri.UriSchemeHttps || uri.Segments.Length < 2 || MultiTenant.Contains(uri.Segments[1].Trim('/'), StringComparer.OrdinalIgnoreCase)))
            return "For Microsoft Entra ID, the issuer must name one tenant, such as https://login.microsoftonline.com/{tenant id}/v2.0.";
        return null;
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
