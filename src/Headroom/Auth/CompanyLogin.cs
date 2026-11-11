using System.Text.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headroom.Auth;

// The Company login: one OpenID Connect provider per Instance, set by a Planner.
// The handler reads its settings from the database at each request, so a change needs no restart.
public static class CompanyLogin
{
    public const string Scheme = "CompanyLogin";
    public const string CallbackPath = "/signin-oidc";
    public const string StartPath = "/login/company";
    public const string TestPath = "/login/company/test";

    // Login code and Passkey sessions last 30 days and slide. A Company login session does not slide.
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private const string SecretPurpose = "Headroom.CompanyLoginSecret";
    private static readonly HttpClient SharedBackchannel = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static AuthenticationBuilder AddCompanyLogin(this AuthenticationBuilder auth)
    {
        auth.Services.AddHttpContextAccessor();
        auth.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, OptionsSetup>();
        // No cache: each request builds the options again from the database. The settings of a test login live in a cookie.
        // shortcut: the provider's discovery document is fetched on each login, cache the options per settings version if logins get slow.
        auth.Services.AddSingleton<IOptionsMonitorCache<OpenIdConnectOptions>, NoCache>();
        return auth.AddOpenIdConnect(Scheme, o =>
        {
            o.Events.OnTicketReceived = CompanyLoginCallback.TicketReceived;
            o.Events.OnRemoteFailure = CompanyLoginCallback.RemoteFailure;
        });
    }

    public static AuthenticationProperties SessionProperties(TimeProvider time) =>
        new() { ExpiresUtc = time.GetUtcNow() + SessionLifetime, AllowRefresh = false };

    public static string? ProtectSecret(string? secret, IDataProtectionProvider protection) =>
        string.IsNullOrEmpty(secret) ? null : protection.CreateProtector(SecretPurpose).Protect(secret);

    public static string? ReadSecret(string? protectedSecret, IDataProtectionProvider protection) =>
        protectedSecret is null ? null : protection.CreateProtector(SecretPurpose).Unprotect(protectedSecret);

    // Settings that a test login uses. They wait in a cookie until the test login ends, and are never stored before.
    // PersonId is the Planner who saves in Settings. CompanyName is set on the first-run screen.
    public record Pending(CompanyLoginKind Kind, string Issuer, string ClientId, string ClientSecret, Guid? PersonId, string? CompanyName)
    {
        // The page that a test login returns to.
        public string ReturnPath => PersonId is not null ? "/settings" : "/first-run";

        private const string CookieName = "headroom.companylogin";
        private static readonly TimeSpan Life = TimeSpan.FromMinutes(10);

        private static ITimeLimitedDataProtector Protector(HttpContext http) =>
            http.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("Headroom.CompanyLoginTest").ToTimeLimitedDataProtector();

        public void Write(HttpContext http) =>
            http.Response.Cookies.Append(CookieName, Protector(http).Protect(JsonSerializer.Serialize(this), Life),
                new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = Life });

        public static Pending? Read(HttpContext http)
        {
            if (!http.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
            try { return JsonSerializer.Deserialize<Pending>(Protector(http).Unprotect(value)); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { return null; }
        }

        public static void Delete(HttpContext http) =>
            http.Response.Cookies.Delete(CookieName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });
    }

    private sealed class OptionsSetup(IHttpContextAccessor accessor, IDataProtectionProvider protection) : IConfigureNamedOptions<OpenIdConnectOptions>
    {
        private const string UnsetIssuer = "https://company-login.invalid";

        public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

        public void Configure(string? name, OpenIdConnectOptions o)
        {
            if (name != Scheme) return;
            o.CallbackPath = CallbackPath;
            o.ResponseType = "code";
            o.UsePkce = true;
            o.MapInboundClaims = false;
            o.GetClaimsFromUserInfoEndpoint = false;
            o.SaveTokens = false;
            // The default actions delete claims such as iss from the principal. The identity rule needs them.
            o.ClaimActions.Clear();
            o.Scope.Clear();
            foreach (var scope in new[] { "openid", "profile", "email" }) o.Scope.Add(scope);
            o.Backchannel = SharedBackchannel;
            var settings = accessor.HttpContext is { } http ? Current(http) : null;
            o.Authority = settings?.Issuer ?? UnsetIssuer;
            o.ClientId = settings?.ClientId ?? "unset";
            o.ClientSecret = settings?.ClientSecret;
            // ValidateIssuer allows plain http only for a local provider.
            o.RequireHttpsMetadata = !o.Authority.StartsWith("http://", StringComparison.Ordinal);
        }

        private Pending? Current(HttpContext http)
        {
            var path = http.Request.Path;
            var isTestOrCallback = path.Equals(TestPath, StringComparison.Ordinal) || path.Equals(CallbackPath, StringComparison.Ordinal);
            if (isTestOrCallback && Pending.Read(http) is { } pending) return pending;
            if (!isTestOrCallback && !path.Equals(StartPath, StringComparison.Ordinal)) return null;
            var row = http.RequestServices.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().FirstOrDefault();
            return row?.CompanyLoginKind is { } kind
                ? new Pending(kind, row.CompanyLoginIssuer!, row.CompanyLoginClientId!, ReadSecret(row.CompanyLoginClientSecret, protection) ?? "", null, null)
                : null;
        }
    }

    private sealed class NoCache : IOptionsMonitorCache<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions GetOrAdd(string? name, Func<OpenIdConnectOptions> createOptions) => createOptions();
        public bool TryAdd(string? name, OpenIdConnectOptions options) => false;
        public bool TryRemove(string? name) => false;
        public void Clear() { }
    }
}
