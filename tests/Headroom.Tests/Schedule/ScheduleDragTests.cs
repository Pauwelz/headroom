using Headroom.Client.Api;
using Headroom.Client.Schedule;

namespace Headroom.Tests.Schedule;

public class ScheduleDragTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static DateOnly Day(int n) => Mon.AddDays(n);

    // The two kinds differ only in the Project: Time off has none.
    public static TheoryData<bool> Kinds => [false, true];

    // The bar spans day 2 to day 5. The drag starts on anchor.
    private static ScheduleDrag<object> Drag(bool allocation, DragMode mode, int anchor) =>
        new(Person, allocation ? Project : null, new object(), Day(2), Day(5), mode, Day(anchor));

    private static bool Enter(ScheduleDrag<object> d, bool allocation, int day) =>
        d.Enter(Person, allocation ? Project : null, Day(day));

    [Theory, MemberData(nameof(Kinds))]
    public void MoveShiftsBothDaysByThePointerDistance(bool allocation)
    {
        var d = Drag(allocation, DragMode.Move, 3);

        Enter(d, allocation, 6);

        Assert.Equal((Day(5), Day(8)), d.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void StartMovesTheFirstDay(bool allocation)
    {
        var d = Drag(allocation, DragMode.Start, 2);

        Enter(d, allocation, 4);

        Assert.Equal((Day(4), Day(5)), d.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void EndMovesTheLastDay(bool allocation)
    {
        var d = Drag(allocation, DragMode.End, 5);

        Enter(d, allocation, 8);

        Assert.Equal((Day(2), Day(8)), d.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void StartPastTheLastDayKeepsTheLastGoodDay(bool allocation)
    {
        var d = Drag(allocation, DragMode.Start, 2);
        Enter(d, allocation, 4);

        Enter(d, allocation, 7);

        Assert.Equal((Day(4), Day(5)), d.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void EndBeforeTheFirstDayKeepsTheLastGoodDay(bool allocation)
    {
        var d = Drag(allocation, DragMode.End, 5);
        Enter(d, allocation, 3);

        Enter(d, allocation, 0);

        Assert.Equal((Day(2), Day(3)), d.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void ResizeToTheOtherEdgeIsAllowed(bool allocation)
    {
        var start = Drag(allocation, DragMode.Start, 2);
        var end = Drag(allocation, DragMode.End, 5);

        Enter(start, allocation, 5);
        Enter(end, allocation, 2);

        Assert.Equal((Day(5), Day(5)), start.Dates);
        Assert.Equal((Day(2), Day(2)), end.Dates);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void AnotherRowIsIgnored(bool allocation)
    {
        var d = Drag(allocation, DragMode.Move, 3);

        Assert.False(d.Enter(Guid.NewGuid(), allocation ? Project : null, Day(6)));
        Assert.False(d.Enter(Person, allocation ? Guid.NewGuid() : Project, Day(6)));
        Assert.Equal((Day(2), Day(5)), d.Dates);
    }

    [Theory]
    [InlineData(DragMode.Move, true)]
    [InlineData(DragMode.Start, false)]
    [InlineData(DragMode.End, false)]
    public void OnlyAMoveWithoutMovementIsAClick(DragMode mode, bool click)
    {
        Assert.Equal(click, Drag(true, mode, 3).IsClick);
        Assert.Equal(click, Drag(false, mode, 3).IsClick);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void MovingAwayAndBackIsAClickAgain(bool allocation)
    {
        var d = Drag(allocation, DragMode.Move, 3);

        Enter(d, allocation, 6);
        Assert.False(d.IsClick);
        Enter(d, allocation, 3);
        Assert.True(d.IsClick);
    }

    [Theory]
    [InlineData(DragMode.Move, 3, false)]
    [InlineData(DragMode.Start, 2, false)] // edge dropped on its own day
    [InlineData(DragMode.End, 5, false)]
    [InlineData(DragMode.Move, 4, true)]
    [InlineData(DragMode.Start, 3, true)]
    [InlineData(DragMode.End, 6, true)]
    public void DropChangesTheBarOnlyWhenTheDatesDiffer(DragMode mode, int drop, bool changed)
    {
        foreach (var allocation in new[] { false, true })
        {
            var d = Drag(allocation, mode, mode == DragMode.End ? 5 : mode == DragMode.Start ? 2 : 3);
            Enter(d, allocation, drop);
            Assert.Equal(changed, d.Changed);
        }
    }
}
