using Headroom.Allocations;

namespace Headroom.Tests.Capacity;

public class AllocationRulesTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    [Fact]
    public void E5_ANewAllocationSplitsTheOldOneAroundIt()
    {
        var old = new AllocationSlice(Guid.NewGuid(), March(1), March(31), 50);

        var result = AllocationRules.Place([old], null, March(10), March(14), 100).OrderBy(a => a.Start).ToList();

        Assert.Equal([(March(1), March(9), 50), (March(10), March(14), 100), (March(15), March(31), 50)],
            result.Select(a => (a.Start, a.End, a.Percentage)));
        Assert.Equal(old.Id, result[0].Id);
        Assert.Null(result[1].Id);
        Assert.Null(result[2].Id);
    }

    [Fact]
    public void AnOverlapAtTheEndShortensTheOldAllocation()
    {
        var old = new AllocationSlice(Guid.NewGuid(), March(1), March(10), 50);

        var result = AllocationRules.Place([old], null, March(8), March(20), 25).OrderBy(a => a.Start).ToList();

        Assert.Equal([(March(1), March(7)), (March(8), March(20))], result.Select(a => (a.Start, a.End)));
    }

    [Fact]
    public void AnOverlapAtTheStartShortensTheOldAllocation()
    {
        var old = new AllocationSlice(Guid.NewGuid(), March(5), March(10), 50);

        var result = AllocationRules.Place([old], null, March(1), March(7), 25).OrderBy(a => a.Start).ToList();

        Assert.Equal([(March(1), March(7)), (March(8), March(10))], result.Select(a => (a.Start, a.End)));
        Assert.Equal(old.Id, result[1].Id);
    }

    [Fact]
    public void ACoveredAllocationIsRemoved()
    {
        var old = new AllocationSlice(Guid.NewGuid(), March(5), March(6), 50);

        Assert.Single(AllocationRules.Place([old], null, March(1), March(10), 25));
    }

    [Fact]
    public void AdjacentAllocationsStayApart()
    {
        var old = new AllocationSlice(Guid.NewGuid(), March(1), March(5), 50);

        Assert.Equal(2, AllocationRules.Place([old], null, March(6), March(9), 50).Count);
    }

    [Fact]
    public void ChangingAnAllocationDoesNotCutItself()
    {
        var id = Guid.NewGuid();
        var old = new AllocationSlice(id, March(1), March(10), 50);

        var result = AllocationRules.Place([old], id, March(1), March(10), 75);

        Assert.Equal([new AllocationSlice(id, March(1), March(10), 75)], result);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(105, false)]
    public void PercentageIs5To100InStepsOf5(int percentage, bool valid) =>
        Assert.Equal(valid, AllocationRules.IsValidPercentage(percentage));
}
