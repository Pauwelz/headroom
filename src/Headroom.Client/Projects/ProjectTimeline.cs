using Headroom.Client.Api;

namespace Headroom.Client.Projects;

// A bar on the small timeline of a Person. Left and Width are percentages of the Project span.
public record TimelineBar(double Left, double Width, int Percentage);

public static class ProjectTimeline
{
    // The span runs from the first to the last Allocation of the Project, end day included.
    public static List<TimelineBar> Bars(DateOnly start, DateOnly end, IEnumerable<ScheduleAllocation> allocations)
    {
        double days = end.DayNumber - start.DayNumber + 1;
        return allocations
            .Select(a => new TimelineBar((a.StartDate.DayNumber - start.DayNumber) / days * 100,
                (a.EndDate.DayNumber - a.StartDate.DayNumber + 1) / days * 100, a.Percentage))
            .ToList();
    }
}
