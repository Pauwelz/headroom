using System.Security.Cryptography;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Headroom.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// The WebAuthn ceremonies of ADR 0015. The options of a ceremony travel in a Data Protection cookie, not in a table:
// it lives 5 minutes and the first verify deletes it. The registration is here. The login ceremony reuses the cookie methods.
public class PasskeyCeremony(IFido2 fido2, IDataProtectionProvider protection)
{
    private static readonly TimeSpan ChallengeLife = TimeSpan.FromMinutes(5);

    public const string RegisterPurpose = "register";

    // The relying party is the Instance, never the Host header. A changed Instance URL invalidates every Passkey.
    public static void AddPasskeys(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var instance = new Uri(sp.GetRequiredService<OAuthSetup.Resources>().Instance);
            return new Fido2Configuration { RPID = instance.Host, RPName = "Headroom", Origins = new HashSet<string> { instance.GetLeftPart(UriPartial.Authority) } };
        });
        services.AddSingleton<IFido2, Fido2>();
        services.AddSingleton<PasskeyCeremony>();
    }

    public static byte[] UserHandle(Person person) => person.Id.ToByteArray();

    // Returns the options JSON for the browser and keeps it in the challenge cookie.
    public string BeginRegistration(HttpContext http, Person person, IEnumerable<Passkey> existing, string cookiePath)
    {
        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = UserHandle(person), Name = person.Email!, DisplayName = person.Name },
            ExcludeCredentials = existing.Select(p => new PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, p.CredentialId, p.Transports.Select(Enum.Parse<AuthenticatorTransport>).ToArray())).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Preferred },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = options.ToJson();
        SaveChallenge(http, RegisterPurpose, cookiePath, person.Id + "|" + json);
        return json;
    }

    // Returns the new credential, or null when the challenge is missing, old, reused or the response does not verify.
    public async Task<RegisteredPublicKeyCredential?> CompleteRegistrationAsync(HttpContext http, Person person, string responseJson, string cookiePath, HeadroomDbContext db)
    {
        var payload = TakeChallenge(http, RegisterPurpose, cookiePath)?.Split('|', 2);
        if (payload is not [var owner, var optionsJson] || owner != person.Id.ToString()) return null;
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(responseJson);
            return await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response!,
                OriginalOptions = CredentialCreateOptions.FromJson(optionsJson),
                IsCredentialIdUniqueToUserCallback = async (p, ct) => !await db.Passkeys.AnyAsync(x => x.CredentialId == p.CredentialId, ct),
            });
        }
        catch (Exception e) when (e is Fido2VerificationException or JsonException or ArgumentException or NullReferenceException) { return null; }
    }

    public void SaveChallenge(HttpContext http, string purpose, string cookiePath, string payload) =>
        http.Response.Cookies.Append(CookieName(purpose), Protector(purpose).Protect(payload, ChallengeLife), CookieOptions(cookiePath, ChallengeLife));

    // Deletes the cookie, so a challenge works once. A missing, old or changed cookie gives null.
    public string? TakeChallenge(HttpContext http, string purpose, string cookiePath)
    {
        var value = http.Request.Cookies[CookieName(purpose)];
        http.Response.Cookies.Delete(CookieName(purpose), CookieOptions(cookiePath, null));
        if (value is null) return null;
        try { return Protector(purpose).Unprotect(value); }
        catch (CryptographicException) { return null; }
    }

    private ITimeLimitedDataProtector Protector(string purpose) =>
        protection.CreateProtector("Headroom.Passkey." + purpose).ToTimeLimitedDataProtector();

    private static string CookieName(string purpose) => "headroom.passkey." + purpose;

    private static CookieOptions CookieOptions(string path, TimeSpan? life) =>
        new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = path, MaxAge = life };
}
