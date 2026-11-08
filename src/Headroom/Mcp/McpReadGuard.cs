using System.Security.Claims;
using Headroom.Auth;
using ModelContextProtocol;

namespace Headroom.Mcp;

// The guard of every MCP tool that only a Planner may read. It does not need Can change.
public static class McpReadGuard
{
    public static void RequirePlanner(IHttpContextAccessor http) => RequirePlanner(http.HttpContext!.User, "read this data");

    // The one Planner check of the read and write guards. The action fills the message.
    public static void RequirePlanner(ClaimsPrincipal user, string action)
    {
        if (!user.IsInRole(PersonPrincipal.PlannerRole))
            throw new McpException($"Only a Planner can {action}. The Person of this token is not a Planner.");
    }
}
