using Headroom.Client.Api;
using Headroom.Client.Schedule;

namespace Headroom.Tests.Schedule;

public class TimeOffChangesTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static ScheduleTimeOff Bar(int start, string startPart, int end, string endPart) =>
        new(Guid.NewGuid(), Mon.AddDays(start), startPart, Mon.AddDays(end), endPart);

    [Fact]
    public void TwoClicksMakeWholeDaysInEitherOrder()
    {
        var expected = new TimeOffRequest(Mon, "morning", Mon.AddDays(2), "afternoon");

        Assert.Equal(expected, TimeOffChanges.Create(Mon, Mon.AddDays(2)));
        Assert.Equal(expected, TimeOffChanges.Create(Mon.AddDays(2), Mon));
    }

    [Fact]
    public void MovingAndResizingKeepTheHalfDays()
    {
        var bar = Bar(1, "afternoon", 3, "morning");

        Assert.Equal(new TimeOffRequest(Mon.AddDays(3), "afternoon", Mon.AddDays(5), "morning"), TimeOffChanges.Move(bar, 2));
        Assert.Equal(new TimeOffRequest(Mon, "afternoon", Mon.AddDays(3), "morning"), TimeOffChanges.ResizeStart(bar, Mon));
        Assert.Equal(new TimeOffRequest(Mon.AddDays(1), "afternoon", Mon.AddDays(4), "morning"), TimeOffChanges.ResizeEnd(bar, Mon.AddDays(4)));
    }

    [Fact]
    public void FirstAndLastDayToggleBetweenHalfAndFull()
    {
        var bar = Bar(1, "morning", 3, "afternoon");

        Assert.Equal("afternoon", TimeOffChanges.FirstDay(bar, half: true).StartPart);
        Assert.Equal("morning", TimeOffChanges.LastDay(bar, half: true).EndPart);
        Assert.Equal("morning", TimeOffChanges.FirstDay(Bar(1, "afternoon", 3, "afternoon"), half: false).StartPart);
        Assert.False(TimeOffChanges.FirstDayIsHalf(bar));
        Assert.True(TimeOffChanges.LastDayIsHalf(Bar(1, "morning", 3, "morning")));
    }

    [Theory]
    [InlineData("morning", "morning", "morning")]
    [InlineData("afternoon", "afternoon", "afternoon")]
    [InlineData("full", "morning", "afternoon")]
    public void ASingleDayCanBeMorningAfternoonOrFull(string choice, string start, string end)
    {
        var bar = Bar(1, "morning", 1, "afternoon");

        Assert.Equal(new TimeOffRequest(Mon.AddDays(1), start, Mon.AddDays(1), end), TimeOffChanges.SingleDay(bar, choice));
    }
}
