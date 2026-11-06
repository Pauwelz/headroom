using Headroom.Client.Api;
using Headroom.Client.Projects;

namespace Headroom.Tests.Shell;

public class ProjectTimelineTests
{
    [Fact]
    public void BarsAreSizedAgainstTheProjectSpanWithTheEndDayIncluded()
    {
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 3, 10);
        var bars = ProjectTimeline.Bars(start, end, [new ScheduleAllocation(Guid.NewGuid(), new(2026, 3, 6), new(2026, 3, 10), 50)]);

        var bar = Assert.Single(bars);
        Assert.Equal(50, bar.Left, 6);
        Assert.Equal(50, bar.Width, 6);
        Assert.Equal(50, bar.Percentage);
    }
}
