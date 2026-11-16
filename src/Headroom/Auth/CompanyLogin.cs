using System.ComponentModel.DataAnnotations;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Headroom.Auth;

// The Entra ID settings from the Operator environment (EntraId__*). Startup fails when a required one is missing.
public class EntraIdOptions
{
    public const string Section = "EntraId";

    [Required] public string TenantId { get; set; } = "";
    [Required] public string ClientId { get; set; } = "";

    // Empty: the system-assigned managed identity.
    public string? ManagedIdentityClientId { get; set; }

    // When set, the login sends this secret and no managed identity is used.
    public string? ClientSecret { get; set; }

    // Only a local provider needs this.
    public string? Authority { get; set; }

    public string AuthorityUrl => string.IsNullOrWhiteSpace(Authority) ? $"https://login.microsoftonline.com/{TenantId}/v2.0" : Authority;
}

// Gives the federated credential that proves the app to Entra ID without a secret. A test replaces it.
public interface IClientAssertionProvider
{
    Task<string> GetAsync(CancellationToken ct);
}

public sealed class ManagedIdentityAssertion(IOptions<EntraIdOptions> options) : IClientAssertionProvider
{
    private readonly ManagedIdentityCredential _credential = new(string.IsNullOrWhiteSpace(options.Value.ManagedIdentityClientId)
        ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(options.Value.ManagedIdentityClientId));

    public async Task<string> GetAsync(CancellationToken ct) =>
        (await _credential.GetTokenAsync(new TokenRequestContext(["api://AzureADTokenExchange/.default"]), ct)).Token;
}

// The Company login: Microsoft Entra ID, the only login. The settings come from EntraIdOptions.
public static class CompanyLogin
{
    public const string Scheme = "CompanyLogin";
    public const string CallbackPath = "/signin-oidc";
    public const string StartPath = "/login/company";

    public static AuthenticationBuilder AddCompanyLogin(this AuthenticationBuilder auth)
    {
        auth.Services.AddOptions<EntraIdOptions>().BindConfiguration(EntraIdOptions.Section).ValidateDataAnnotations()
            .Validate(o => Guid.TryParse(o.TenantId, out _), "EntraId:TenantId must be a GUID.").ValidateOnStart();
        auth.Services.AddSingleton<IClientAssertionProvider, ManagedIdentityAssertion>();
        auth.Services.AddOptions<OpenIdConnectOptions>(Scheme).Configure<IOptions<EntraIdOptions>>((o, entra) =>
        {
            o.Authority = entra.Value.AuthorityUrl;
            o.ClientId = entra.Value.ClientId;
            o.ClientSecret = string.IsNullOrEmpty(entra.Value.ClientSecret) ? null : entra.Value.ClientSecret;
            // Plain http is only for a local provider.
            o.RequireHttpsMetadata = !o.Authority.StartsWith("http://", StringComparison.Ordinal);
        });
        return auth.AddOpenIdConnect(Scheme, o =>
        {
            o.CallbackPath = CallbackPath;
            o.ResponseType = "code";
            o.UsePkce = true;
            o.MapInboundClaims = false;
            o.GetClaimsFromUserInfoEndpoint = false;
            o.SaveTokens = false;
            // The default actions delete claims such as iss from the principal. The identity rule needs them.
            o.ClaimActions.Clear();
            o.Scope.Clear();
            foreach (var scope in new[] { "openid", "profile" }) o.Scope.Add(scope);
            o.Events.OnAuthorizationCodeReceived = async context =>
            {
                if (!string.IsNullOrEmpty(context.Options.ClientSecret)) return;
                context.TokenEndpointRequest!.ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
                context.TokenEndpointRequest.ClientAssertion = await context.HttpContext.RequestServices
                    .GetRequiredService<IClientAssertionProvider>().GetAsync(context.HttpContext.RequestAborted);
            };
            o.Events.OnTicketReceived = CompanyLoginCallback.TicketReceived;
            o.Events.OnRemoteFailure = CompanyLoginCallback.RemoteFailure;
        });
    }
}
