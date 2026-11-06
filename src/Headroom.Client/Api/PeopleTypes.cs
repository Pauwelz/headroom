namespace Headroom.Client.Api;

// Shared API types of the People endpoints. Field names are a contract.

/// <summary>Contract hours for Monday to Sunday.</summary>
public record WeekHours(
    decimal Monday, decimal Tuesday, decimal Wednesday, decimal Thursday,
    decimal Friday, decimal Saturday, decimal Sunday)
{
    public decimal Total => Monday + Tuesday + Wednesday + Thursday + Friday + Saturday + Sunday;

    // Each day is 0 to 24 hours in steps of 0.25 hour.
    public bool IsValid => ToArray().All(h => h is >= 0 and <= 24 && h % 0.25m == 0);

    public decimal[] ToArray() => [Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday];

    public static WeekHours From(IReadOnlyList<decimal> h) => new(h[0], h[1], h[2], h[3], h[4], h[5], h[6]);

    public static readonly WeekHours FullTime = new(8, 8, 8, 8, 8, 0, 0);

    public static readonly IReadOnlyList<(string Label, WeekHours Hours)> Presets =
    [
        ("8 h Mon–Fri", FullTime),
        ("4 days", new(8, 8, 8, 8, 0, 0, 0)),
        ("Half time", new(4, 4, 4, 4, 4, 0, 0)),
        ("0 h", new(0, 0, 0, 0, 0, 0, 0)),
    ];
}

/// <summary>EndDate is the day before the next Contract starts. It is null for the last Contract.</summary>
public record ContractResponse(
    string Type, Guid Id, Guid PersonId, DateOnly StartDate, DateOnly? EndDate,
    WeekHours Hours, decimal HoursPerWeek, Guid? LastChangedBy, DateTimeOffset DateModified);

/// <summary>
/// Why a lifecycle action is not possible now. The server computes each reason.
/// A null reason means the action is allowed.
/// </summary>
public record PersonLocks(string? SetEndDate, string? RemoveEndDate, string? Rehire, string? Erase, string? PlannerFlag);

/// <summary>Name is "Former Person N" and Email is null for an erased Person. Contracts are sorted by start date.</summary>
public record PersonResponse(
    string Type, Guid Id, string Name, string? Email, bool IsPlanner, DateOnly? EndDate, int? ErasedNumber,
    string Status, bool CanLogIn, decimal HoursPerWeekNow, PersonLocks Locks, IReadOnlyList<ContractResponse> Contracts,
    Guid? LastChangedBy, DateTimeOffset DateModified);

public record AddPersonRequest(string Name, string Email, bool IsPlanner, DateOnly StartDate, WeekHours Hours);

public record EditPersonRequest(string Name, string Email, bool IsPlanner);

public record ContractRequest(DateOnly StartDate, WeekHours Hours);

public record SetEndDateRequest(DateOnly EndDate);

/// <summary>One record that the end date cuts. Deleted is false when the record is shortened.</summary>
public record CutRecord(string Kind, string Description, bool Deleted);

/// <summary>What a new end date does. The login stops the day after the last working day.</summary>
public record EndDateImpact(DateOnly LastWorkingDay, DateOnly LoginStopsOn, IReadOnlyList<CutRecord> Records);

/// <summary>The status values of a Person.</summary>
public static class PersonStatus
{
    public const string Active = "Active";
    public const string StartsLater = "Starts later";
    public const string Leaving = "Leaving";
    public const string Left = "Left";
    public const string Erased = "Erased";

    public static string Of(int? erasedNumber, DateOnly? endDate, DateOnly? firstContractStart, DateOnly today) =>
        erasedNumber is not null ? Erased
        : endDate < today ? Left
        : endDate is not null ? Leaving
        : firstContractStart > today ? StartsLater
        : Active;
}
