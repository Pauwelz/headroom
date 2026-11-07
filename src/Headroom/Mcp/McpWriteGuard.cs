using System.Security.Claims;
using Headroom.Auth;
using ModelContextProtocol;

namespace Headroom.Mcp;

// The guards of every MCP tool that writes. Each one returns who acts, or throws a tool error.
public static class McpWriteGuard
{
    // Can change: needed by every write tool. What the Person may write is up to the tool.
    public static ClaimsPrincipal CanChange(IHttpContextAccessor http) => CanChange(http.HttpContext!.User);

    public static ClaimsPrincipal CanChange(ClaimsPrincipal user) => user.CanChange()
        ? user
        : throw new McpException("This token is read only. Use a token or Connection with \"Can change\" to change data.");

    public static Guid PlannerId(IHttpContextAccessor http) => PlannerId(http.HttpContext!.User);

    // The Planner check comes first. "Not a Planner" is the clearer message.
    public static Guid PlannerId(ClaimsPrincipal user)
    {
        if (!user.IsInRole(PersonPrincipal.PlannerRole))
            throw new McpException("Only a Planner can change data. The Person of this token is not a Planner.");
        return CanChange(user).PersonId()!.Value; // authentication fails without a Person
    }
}
