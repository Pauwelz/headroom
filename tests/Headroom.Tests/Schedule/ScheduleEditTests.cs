using Headroom.Client.Schedule;

namespace Headroom.Tests.Schedule;

public class ScheduleEditTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void RowRightsFollowRoleAndOwnership(bool isPlanner, bool ownRow, bool timeOff, bool allocations) =>
        Assert.Equal(new ScheduleEdit(timeOff, allocations), ScheduleEdit.For(isPlanner, ownRow));
}
