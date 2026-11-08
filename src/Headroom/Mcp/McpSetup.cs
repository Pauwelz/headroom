using Headroom.Auth;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace Headroom.Mcp;

// The MCP server at /mcp. Only bearer tokens work (Access tokens and Connected apps): the login cookie does not work here.
public static class McpSetup
{
    public const string Path = "/mcp";

    public static void AddHeadroomMcp(this IServiceCollection services)
    {
        services.AddHttpContextAccessor(); // get_me reads the caller
        // The MCP scheme publishes the RFC 9728 metadata and challenges with it. It checks the token through the bearer selector.
        services.AddAuthentication().AddMcp(o => o.ForwardAuthenticate = OAuthAuth.BearerScheme);
        services.AddOptions<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme).Configure<OAuthSetup.Resources>((o, r) =>
            o.ResourceMetadata = new ProtectedResourceMetadata { Resource = r.Mcp, AuthorizationServers = [r.Issuer] });
        services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<ScheduleTools>()
            .WithTools<AllocationTools>()
            .WithTools<TimeOffTools>()
            .WithTools<ClientProjectTools>()
            .WithTools<PeopleTools>()
            .WithTools<CompanyDayOffTools>();
    }

    public static void MapHeadroomMcp(this IEndpointRouteBuilder app) => app.MapMcp(Path)
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = McpAuthenticationDefaults.AuthenticationScheme });

    // A browser sends Origin. Only the Headroom origin may call /mcp, which stops DNS rebinding.
    // Claude calls from a server and sends no Origin.
    public static void UseMcpOriginCheck(this IApplicationBuilder app) =>
        app.UseWhen(http => http.Request.Path.StartsWithSegments(Path), branch => branch.Use(async (http, next) =>
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !string.Equals(origin, $"{http.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        }));
}
