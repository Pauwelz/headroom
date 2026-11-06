namespace Headroom.Allocations;

/// <summary>An Allocation of one Person on one Project. Id is null for a new one.</summary>
public sealed record AllocationSlice(Guid? Id, DateOnly Start, DateOnly End, int Percentage);

/// <summary>Allocation placement rules. Plain inputs, no database.</summary>
public static class AllocationRules
{
    public static bool IsValidPercentage(int percentage) => percentage is >= 5 and <= 100 && percentage % 5 == 0;

    /// <summary>
    /// Places a new or changed Allocation on a Person and Project. The latest change wins on its days:
    /// an existing Allocation is shortened, split around it, or removed when it is fully covered.
    /// Adjacent Allocations do not merge. <paramref name="existing"/> holds the current Allocations of this
    /// Person and Project. Pass the id of the changed one as <paramref name="changedId"/>, or null for a new one.
    /// Returns the Allocations that remain: those with an id are updates, those without are new.
    /// </summary>
    public static IReadOnlyList<AllocationSlice> Place(
        IEnumerable<AllocationSlice> existing, Guid? changedId, DateOnly start, DateOnly end, int percentage)
    {
        var result = new List<AllocationSlice>();
        foreach (var other in existing.Where(a => a.Id != changedId))
        {
            if (other.End < start || other.Start > end) { result.Add(other); continue; }
            var keepsId = true;
            if (other.Start < start) { result.Add(other with { End = start.AddDays(-1) }); keepsId = false; }
            if (other.End > end) result.Add(other with { Id = keepsId ? other.Id : null, Start = end.AddDays(1) });
        }
        result.Add(new AllocationSlice(changedId, start, end, percentage));
        return result;
    }
}
