using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;

namespace Headroom.Tests.People;

public class PersonLockRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static Person Person(DateOnly? end = null, int? erased = null) => new() { EndDate = end, ErasedNumber = erased };

    [Fact]
    public void AnActivePersonHasOnlyTheLocksThatNeedAnEndDate()
    {
        var locks = PersonLockRules.Of(Person(), false, Today);

        Assert.Equal(new PersonLocks(null, PersonLockRules.NoEndDate, PersonLockRules.NotLeft, PersonLockRules.EraseNeedsEndDate, null), locks);
    }

    [Fact]
    public void ALeavingPersonCanLoseTheEndDateAndCanBeErasedOnTheEndDate()
    {
        Assert.Null(PersonLockRules.RemoveEndDate(Person(Today), Today));
        Assert.Null(PersonLockRules.Erase(Person(Today), false, Today));
        Assert.Equal(PersonLockRules.EraseNeedsEndDate, PersonLockRules.Erase(Person(Today.AddDays(1)), false, Today));
    }

    [Fact]
    public void ALeftPersonCanBeRehiredButGetsNoEndDate()
    {
        var left = Person(Today.AddDays(-1));

        Assert.Null(PersonLockRules.Rehire(left, Today));
        Assert.Equal(PersonLockRules.Left, PersonLockRules.SetEndDate(left, false, Today));
        Assert.Equal(PersonLockRules.EndDatePassed, PersonLockRules.RemoveEndDate(left, Today));
    }

    [Fact]
    public void AnErasedPersonIsLockedEverywhere()
    {
        var locks = PersonLockRules.Of(Person(Today.AddDays(-1), 1), false, Today);

        Assert.All([locks.SetEndDate, locks.Rehire, locks.Erase, locks.PlannerFlag], Assert.NotNull);
    }

    [Fact]
    public void TheLastPlannerIsLockedForEndDateEraseAndFlag()
    {
        var locks = PersonLockRules.Of(Person(Today), true, Today);

        Assert.Equal(LastPlanner.Message, locks.SetEndDate);
        Assert.Equal(LastPlanner.Message, locks.Erase);
        Assert.Equal(LastPlanner.Message, locks.PlannerFlag);
    }
}
