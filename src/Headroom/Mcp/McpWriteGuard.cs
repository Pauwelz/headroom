using System.Security.Claims;
using Headroom.Auth;
using ModelContextProtocol;

namespace Headroom.Mcp;

// The guards of every MCP tool that writes. Each one returns who acts, or throws a tool error.
public static class McpWriteGuard
{
    // Can change: needed by every write tool. What the Person may write is up to the tool.
    public static ClaimsPrincipal RequireCanChange(IHttpContextAccessor http) => RequireCanChange(http.HttpContext!.User);

    public static ClaimsPrincipal RequireCanChange(ClaimsPrincipal user) => user.CanChange()
        ? user
        : throw new McpException("This token is read only. Use a token or Connection with \"Can change\" to change data.");

    // For writes that need no actor: checks, then gives the value back so a call can use it as an argument.
    public static T AfterPlannerCheck<T>(IHttpContextAccessor http, T value)
    {
        PlannerId(http);
        return value;
    }

    public static Guid PlannerId(IHttpContextAccessor http) => PlannerId(http.HttpContext!.User);

    // The Planner check comes first. "Not a Planner" is the clearer message.
    public static Guid PlannerId(ClaimsPrincipal user)
    {
        McpReadGuard.RequirePlanner(user, "change data");
        return RequireCanChange(user).PersonId()!.Value; // authentication fails without a Person
    }
}
