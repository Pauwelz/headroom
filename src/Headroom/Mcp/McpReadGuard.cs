using Headroom.Auth;
using ModelContextProtocol;

namespace Headroom.Mcp;

// The guard of every MCP tool that only a Planner may read. It does not need Can change.
public static class McpReadGuard
{
    public static void RequirePlanner(IHttpContextAccessor http)
    {
        if (!http.HttpContext!.User.IsInRole(PersonPrincipal.PlannerRole))
            throw new McpException("Only a Planner can read this data. The Person of this token is not a Planner.");
    }
}
