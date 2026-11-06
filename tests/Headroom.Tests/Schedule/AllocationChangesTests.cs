using Headroom.Client.Schedule;
using Headroom.Client.Api;

namespace Headroom.Tests.Schedule;

public class AllocationChangesTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static ScheduleAllocation Bar(int start, int end) => new(Guid.NewGuid(), Mon.AddDays(start), Mon.AddDays(end), 50);

    [Fact]
    public void MovingAndResizingChangeTheDates()
    {
        var bar = Bar(1, 3);

        Assert.Equal((Mon.AddDays(3), Mon.AddDays(5)), AllocationChanges.Move(bar, 2));
        Assert.Equal((Mon, Mon.AddDays(3)), AllocationChanges.ResizeStart(bar, Mon));
        Assert.Equal((Mon.AddDays(1), Mon.AddDays(6)), AllocationChanges.ResizeEnd(bar, Mon.AddDays(6)));
    }

    [Theory]
    [InlineData(2, 2)] // inside: use the wanted day
    [InlineData(1, 2)] // the first day cannot start the second part: use the next day
    [InlineData(0, 2)] // before the bar
    [InlineData(9, 5)] // after the bar: the last day
    public void SplitDayStaysInsideTheBar(int wanted, int expected)
    {
        var bar = Bar(1, 5);

        Assert.Equal(Mon.AddDays(expected), AllocationChanges.SplitDay(bar, Mon.AddDays(wanted)));
    }

    [Fact]
    public void WithoutAWantedDaySplitUsesTheMiddleAndAOneDayBarCannotSplit()
    {
        Assert.Equal(Mon.AddDays(3), AllocationChanges.SplitDay(Bar(1, 4), null));
        Assert.Null(AllocationChanges.SplitDay(Bar(2, 2), Mon.AddDays(2)));
    }
}
