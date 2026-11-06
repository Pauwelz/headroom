namespace Headroom.Capacity;

/// <summary>Time off rules. Plain inputs, no database.</summary>
public static class TimeOffRules
{
    /// <summary>The end is not before the start. A morning-only day has start and end "Morning".</summary>
    public static bool IsValid(TimeOffTerm t) => Last(t) >= First(t);

    /// <summary>Merge the new term with every term it overlaps or touches. The earliest start and the latest end win.
    /// Merged lists the terms that are absorbed, so the caller can delete them.</summary>
    public static (TimeOffTerm Term, IReadOnlyList<T> Merged) Merge<T>(TimeOffTerm added, IEnumerable<T> others, Func<T, TimeOffTerm> term)
    {
        var result = added;
        var pending = others.ToList();
        var merged = new List<T>();
        // A merge can grow the term, so repeat until no other term joins.
        while (pending.FirstOrDefault(o => Joins(result, term(o))) is { } next)
        {
            var t = term(next);
            result = new TimeOffTerm(
                First(t) < First(result) ? t.StartDate : result.StartDate, First(t) < First(result) ? t.StartPart : result.StartPart,
                Last(t) > Last(result) ? t.EndDate : result.EndDate, Last(t) > Last(result) ? t.EndPart : result.EndPart);
            pending.Remove(next);
            merged.Add(next);
        }
        return (result, merged);
    }

    // Half-day slots: 2 per day, morning then afternoon. First and Last are the slots the term covers.
    private static int First(TimeOffTerm t) => t.StartDate.DayNumber * 2 + (t.StartPart == DayPart.Afternoon ? 1 : 0);

    private static int Last(TimeOffTerm t) => t.EndDate.DayNumber * 2 + (t.EndPart == DayPart.Afternoon ? 1 : 0);

    // Overlap, or the slots lie next to each other.
    private static bool Joins(TimeOffTerm a, TimeOffTerm b) => First(a) <= Last(b) + 1 && First(b) <= Last(a) + 1;
}
