using Headroom.Client.Api;
using Headroom.Data;

namespace Headroom.People;

// The reasons a lifecycle action is locked. Each method returns null when the action is allowed.
// The endpoints refuse with the same text, and PersonResponse carries it to the UI.
public static class PersonLockRules
{
    public const string Left = "This Person has left. Use Rehire.";
    public const string NoEndDate = "This Person has no end date.";
    public const string EndDatePassed = "The end date has passed. Use Rehire.";
    public const string NotLeft = "This Person has not left.";

    public static PersonLocks Of(Person p, bool isLastPlanner, DateOnly today) => new(
        SetEndDate(p, isLastPlanner, today), RemoveEndDate(p, today), Rehire(p, today), PlannerFlag(isLastPlanner));

    public static string? SetEndDate(Person p, bool isLastPlanner, DateOnly today) =>
        p.EndDate < today ? Left
        : isLastPlanner ? LastPlanner.Message
        : null;

    public static string? RemoveEndDate(Person p, DateOnly today) =>
        p.EndDate is null ? NoEndDate
        : p.EndDate < today ? EndDatePassed
        : null;

    public static string? Rehire(Person p, DateOnly today) =>
        p.EndDate is not { } end || end >= today ? NotLeft : null;

    public static string? PlannerFlag(bool isLastPlanner) => isLastPlanner ? LastPlanner.Message : null;
}
