using Microsoft.AspNetCore.Authorization;

namespace Headroom.Auth;

// Server-side access rule. Put .RequirePlanner() on every endpoint
// or group that only Planners may use. The UI hides controls, but this check is the real rule.
public static class PlannerPolicy
{
    public const string Name = "Planner";

    public static void AddPlannerPolicy(this AuthorizationOptions options) =>
        options.AddPolicy(Name, p => p.RequireRole(PersonPrincipal.PlannerRole));

    public static TBuilder RequirePlanner<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Name);
}
