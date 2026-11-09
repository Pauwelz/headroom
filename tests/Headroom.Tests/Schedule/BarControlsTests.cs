using Headroom.Client.Schedule;

namespace Headroom.Tests.Schedule;

public class BarControlsTests
{
    // The visible days in these tests: Mon 2 March to Sun 15 March 2026.
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly DateOnly Sun = Mon.AddDays(13);

    private static BarControls Of(int first, int last) => BarControls.Of(Mon.AddDays(first), Mon.AddDays(last), Mon, Sun);

    [Fact]
    public void ABarInsideTheVisibleDaysKeepsItsPercentageAtItsStart()
    {
        Assert.Equal(new BarControls(0, 5), Of(2, 6));
    }

    [Fact]
    public void ABarThatStartsEarlierMovesItsPercentageToTheFirstVisibleDay()
    {
        Assert.Equal(new BarControls(3, 6), Of(-3, 5));
    }

    [Fact]
    public void ABarThatEndsLaterCountsOnlyItsVisibleDays()
    {
        Assert.Equal(new BarControls(0, 4), Of(10, 17));
    }

    [Fact]
    public void ABarThatCoversTheVisibleDaysMovesItsPercentageAndCountsOnlyItsVisibleDays()
    {
        Assert.Equal(new BarControls(21, 14), Of(-21, 34));
    }

    [Fact]
    public void ABarOutsideTheVisibleDaysKeepsItsPercentageOnItsOwnDays()
    {
        Assert.Equal(new BarControls(2, 1), Of(-5, -3));
        Assert.Equal(new BarControls(0, 1), Of(20, 22));
    }
}
