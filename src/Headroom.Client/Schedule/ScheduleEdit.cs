namespace Headroom.Client.Schedule;

/// <summary>What the viewer can edit on one row. The server enforces the same rule.</summary>
public readonly record struct ScheduleEdit(bool TimeOff, bool Allocations)
{
    public static ScheduleEdit For(bool isPlanner, bool ownRow) => new(isPlanner || ownRow, isPlanner);
}
