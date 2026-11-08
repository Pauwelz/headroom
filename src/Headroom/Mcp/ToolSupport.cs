using Headroom.Client.Api;
using ModelContextProtocol;

namespace Headroom.Mcp;

// What more than one tool class shares: description text and the way a refusal becomes a tool error.
public static class ToolSupport
{
    public const string FirstDay = "First day, yyyy-MM-dd";
    public const string LastDay = "Last day, inclusive, yyyy-MM-dd";
    public const string PersonIdText = "Id of the Person";
    public const string PlannerRights = " Needs an Access token of a Planner with Can change.";

    // A refusal becomes a tool error with the message of the API.
    public static string Done(Refusal? refusal, string message) => refusal is null ? message : throw Error(refusal);

    public static T Value<T>((T? Value, Refusal? Refusal) result) where T : class => result.Value ?? throw Error(result.Refusal!);

    private static McpException Error(Refusal refusal) => new(refusal.Detail);
}
