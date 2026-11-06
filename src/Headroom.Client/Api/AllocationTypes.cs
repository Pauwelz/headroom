namespace Headroom.Client.Api;

// Shared API types for Allocation writes. Field names are a contract.
// Dates are inclusive. Percentage is 5 to 100 in steps of 5.
public record CreateAllocationRequest(Guid PersonId, Guid ProjectId, DateOnly StartDate, DateOnly EndDate, int Percentage);

/// <summary>PersonId and ProjectId are optional: when null, the Allocation keeps its Person or Project.</summary>
public record UpdateAllocationRequest(DateOnly StartDate, DateOnly EndDate, int Percentage, Guid? PersonId = null, Guid? ProjectId = null);

/// <summary>The second Allocation starts on Date. Date is after the start date and not after the end date.</summary>
public record SplitAllocationRequest(DateOnly Date);
