using Headroom.Client.Api;
using Headroom.Data;

namespace Headroom.People;

// The reasons a lifecycle action is locked. Each method returns null when the action is allowed.
// The endpoints refuse with the same text, and PersonResponse carries it to the UI.
public static class PersonLockRules
{
    public const string ErasedNoEndDate = "An erased Person cannot get an end date.";
    public const string Left = "This Person has left. Use Rehire.";
    public const string AlreadyErased = "This Person is already erased.";
    public const string EraseNeedsEndDate = "Erase is possible when the end date is today or earlier.";
    public const string NoEndDate = "This Person has no end date.";
    public const string EndDatePassed = "The end date has passed. Use Rehire.";
    public const string ErasedRehire = "An erased Person cannot be rehired. Add a new Person.";
    public const string NotLeft = "This Person has not left.";
    public const string ErasedDetails = "The details of an erased Person cannot change.";

    public static PersonLocks Of(Person p, bool isLastPlanner, DateOnly today) => new(
        SetEndDate(p, isLastPlanner, today), RemoveEndDate(p, today), Rehire(p, today), Erase(p, isLastPlanner, today),
        PlannerFlag(p, isLastPlanner));

    public static string? SetEndDate(Person p, bool isLastPlanner, DateOnly today) =>
        p.ErasedNumber is not null ? ErasedNoEndDate
        : p.EndDate < today ? Left
        : isLastPlanner ? LastPlanner.Message
        : null;

    public static string? RemoveEndDate(Person p, DateOnly today) =>
        p.EndDate is null ? NoEndDate
        : p.EndDate < today ? EndDatePassed
        : null;

    public static string? Rehire(Person p, DateOnly today) =>
        p.ErasedNumber is not null ? ErasedRehire
        : p.EndDate is not { } end || end >= today ? NotLeft
        : null;

    public static string? Erase(Person p, bool isLastPlanner, DateOnly today) =>
        p.ErasedNumber is not null ? AlreadyErased
        : p.EndDate is not { } end || end > today ? EraseNeedsEndDate
        : isLastPlanner ? LastPlanner.Message
        : null;

    public static string? PlannerFlag(Person p, bool isLastPlanner) =>
        p.ErasedNumber is not null ? ErasedDetails
        : isLastPlanner ? LastPlanner.Message
        : null;
}
