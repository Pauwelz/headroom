using System.Security.Claims;
using Headroom.Auth;
using ModelContextProtocol;

namespace Headroom.Mcp;

// The one guard of every MCP tool that writes. It returns the Person that acts, or throws a tool error.
public static class McpWriteGuard
{
    public static Guid PlannerId(IHttpContextAccessor http) => PlannerId(http.HttpContext!.User);

    // The Planner check comes first. A token of a non-Planner never has Can change.
    // "Not a Planner" is the clearer message.
    public static Guid PlannerId(ClaimsPrincipal user)
    {
        if (!user.IsInRole(PersonPrincipal.PlannerRole))
            throw new McpException("Only a Planner can change data. The Person of this token is not a Planner.");
        if (!user.CanChange())
            throw new McpException("This token is read only. Use a token or Connection with \"Can change\" to change data.");
        return user.PersonId()!.Value; // authentication fails without a Person
    }
}
