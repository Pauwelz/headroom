using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;

namespace Headroom.Tests.People;

public class PersonLockRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static Person Person(DateOnly? end = null) => new() { EndDate = end };

    [Fact]
    public void AnActivePersonHasOnlyTheLocksThatNeedAnEndDate()
    {
        var locks = PersonLockRules.Of(Person(), false, Today);

        Assert.Equal(new PersonLocks(null, PersonLockRules.NoEndDate, PersonLockRules.NotLeft, null), locks);
    }

    [Fact]
    public void ALeavingPersonCanLoseTheEndDate() =>
        Assert.Null(PersonLockRules.RemoveEndDate(Person(Today), Today));

    [Fact]
    public void ALeftPersonCanBeRehiredButGetsNoEndDate()
    {
        var left = Person(Today.AddDays(-1));

        Assert.Null(PersonLockRules.Rehire(left, Today));
        Assert.Equal(PersonLockRules.Left, PersonLockRules.SetEndDate(left, false, Today));
        Assert.Equal(PersonLockRules.EndDatePassed, PersonLockRules.RemoveEndDate(left, Today));
    }

    [Fact]
    public void TheLastPlannerIsLockedForEndDateAndFlag()
    {
        var locks = PersonLockRules.Of(Person(Today), true, Today);

        Assert.Equal(LastPlanner.Message, locks.SetEndDate);
        Assert.Equal(LastPlanner.Message, locks.PlannerFlag);
    }
}
