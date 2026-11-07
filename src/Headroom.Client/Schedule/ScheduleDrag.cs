namespace Headroom.Client.Schedule;

public enum DragMode { Move, Start, End }

/// <summary>A drag on a bar of the Schedule: Time off (no Project) or an Allocation (with a Project).
/// It holds the pick, clamp and drop rules. The grid only draws it and sends the result to the server.</summary>
public sealed class ScheduleDrag<TBar>(Guid personId, Guid? projectId, TBar bar, DateOnly barStart, DateOnly barEnd, DragMode mode, DateOnly anchor)
{
    public TBar Bar { get; } = bar;
    public DragMode Mode { get; } = mode;
    public DateOnly Anchor { get; } = anchor;
    public DateOnly Current { get; private set; } = anchor;

    /// <summary>The dates of the bar now.</summary>
    public (DateOnly Start, DateOnly End) Dates => Mode switch
    {
        DragMode.Move => (barStart.AddDays(Current.DayNumber - Anchor.DayNumber), barEnd.AddDays(Current.DayNumber - Anchor.DayNumber)),
        DragMode.Start => (Current, barEnd),
        _ => (barStart, Current),
    };

    /// <summary>The pointer enters a day. Returns false when it is another row. A resize past the other edge keeps the last good day.</summary>
    public bool Enter(Guid person, Guid? project, DateOnly day)
    {
        if (person != personId || project != projectId) return false;
        var previous = Current;
        Current = day;
        if (Dates is var (start, end) && end < start) Current = previous;
        return true;
    }

    /// <summary>A press and release on the same day, without a move.</summary>
    public bool IsClick => Mode == DragMode.Move && Current == Anchor;

    public bool Changed => Dates != (barStart, barEnd);
}
