using System.Security.Cryptography;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Client.Shell;
using Headroom.Events;
using OpenIddict.Abstractions;
using static Headroom.Endpoints.ApiProblem;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Headroom.Endpoints;

// Connected apps in Settings (ADR 0008). Planners only, and only with the login cookie: a token cannot manage Connected apps.
public static class ConnectedAppEndpoints
{
    public static void MapConnectedAppEndpoints(this RouteGroupBuilder api)
    {
        var apps = api.MapGroup("/settings/connected-apps").RequireAuthorization(AccessTokenAuth.NoTokenPolicy, PlannerPolicy.Name);
        apps.MapGet("", List);
        apps.MapPost("", Add);
        apps.MapPut("/{id}", Change);
        apps.MapPost("/{id}/secret", NewSecret);
        apps.MapDelete("/{id}", Delete);
    }

    private static async Task<IResult> List(IOpenIddictApplicationManager manager)
    {
        var list = new List<ConnectedAppResponse>();
        await foreach (var app in manager.ListAsync())
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, app);
            list.Add(new((await manager.GetIdAsync(app))!, descriptor.DisplayName!, descriptor.ClientId!, RedirectUrls(descriptor)));
        }
        return Results.Ok(list.OrderBy(a => a.Name));
    }

    private static async Task<IResult> Add(ConnectedAppRequest request, IOpenIddictApplicationManager manager, OAuthSetup.Resources resources)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            ClientSecret = NewClientSecret(),
            ClientType = ClientTypes.Confidential,
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
        };
        descriptor.AddResourcePermissions(resources.Mcp, resources.Api);
        if (Apply(request, descriptor) is { } problem) return problem;
        var secret = descriptor.ClientSecret;
        var app = await manager.CreateAsync(descriptor);
        return Results.Ok(ToCreatedResponse((await manager.GetIdAsync(app))!, descriptor, secret));
    }

    // The client ID and secret stay, so the Connections of the app keep working.
    private static async Task<IResult> Change(string id, ConnectedAppRequest request, IOpenIddictApplicationManager manager)
    {
        if (await manager.FindByIdAsync(id) is not { } app) return Problem(404, NotFoundDetail);
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, app);
        if (Apply(request, descriptor) is { } problem) return problem;
        await manager.UpdateAsync(app, descriptor);
        return Results.Ok(new ConnectedAppResponse(id, descriptor.DisplayName!, descriptor.ClientId!, RedirectUrls(descriptor)));
    }

    // Copies the name and the redirect URLs of the request to the descriptor. Returns a problem, and changes nothing, if they are not valid.
    private static IResult? Apply(ConnectedAppRequest request, OpenIddictApplicationDescriptor descriptor)
    {
        if (CleanName(request.Name) is not { } name) return Problem(400, NameDetail);
        var urls = request.RedirectUrls ?? [];
        if (urls.Count == 0) return Problem(400, UrlRules.NoRedirectUrl);
        var redirects = new List<Uri>();
        foreach (var raw in urls)
        {
            if (UrlRules.RedirectUrl(raw, out var url) is { } problem) return Problem(400, problem);
            redirects.Add(url);
        }
        descriptor.DisplayName = name;
        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.UnionWith(redirects);
        return null;
    }

    private static async Task<IResult> NewSecret(string id, IOpenIddictApplicationManager manager)
    {
        if (await manager.FindByIdAsync(id) is not { } app) return Problem(404, NotFoundDetail);
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, app);
        var secret = descriptor.ClientSecret = NewClientSecret();
        await manager.UpdateAsync(app, descriptor);
        return Results.Ok(ToCreatedResponse(id, descriptor, secret));
    }

    private static async Task<IResult> Delete(string id, IOpenIddictApplicationManager manager)
    {
        if (await manager.FindByIdAsync(id) is not { } app) return Problem(404, NotFoundDetail);
        // The store also deletes the Connections and tokens of the app, so its tokens stop working (entry validation).
        // Do not load Connections here first: the bulk delete leaves them tracked and the save then fails.
        await manager.DeleteAsync(app);
        return Results.NoContent();
    }

    private static List<string> RedirectUrls(OpenIddictApplicationDescriptor d) => [.. d.RedirectUris.Select(u => u.OriginalString)];

    private static CreatedConnectedAppResponse ToCreatedResponse(string id, OpenIddictApplicationDescriptor d, string secret) =>
        new(id, d.DisplayName!, d.ClientId!, RedirectUrls(d), secret);

    private static string NewClientSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private const string NotFoundDetail = "The Connected app does not exist.";
}
