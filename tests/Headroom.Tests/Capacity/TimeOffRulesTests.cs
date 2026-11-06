using Headroom.Capacity;

namespace Headroom.Tests.Capacity;

// Worked example E7, and the other Time off rules.
public class TimeOffRulesTests
{
    private const DayPart AM = DayPart.Morning;
    private const DayPart PM = DayPart.Afternoon;
    private static readonly DateOnly Mon = new(2026, 3, 2);

    private static TimeOffTerm Term(int startDay, DayPart startPart, int endDay, DayPart endPart) =>
        new(Mon.AddDays(startDay), startPart, Mon.AddDays(endDay), endPart);

    private static (TimeOffTerm Term, IReadOnlyList<TimeOffTerm> Merged) Merge(TimeOffTerm added, params TimeOffTerm[] others) =>
        TimeOffRules.Merge(added, others, t => t);

    [Fact]
    public void E7_OverlapOnWednesdayAfternoonMergesIntoOne()
    {
        var existing = Term(0, AM, 2, PM);

        var (term, merged) = Merge(Term(2, PM, 4, PM), existing);

        Assert.Equal(Term(0, AM, 4, PM), term);
        Assert.Equal([existing], merged);
    }

    [Fact]
    public void TermsThatTouchMerge()
    {
        Assert.Equal(Term(0, AM, 3, PM), Merge(Term(2, AM, 3, PM), Term(0, AM, 1, PM)).Term);
        Assert.Equal(Term(0, AM, 2, AM), Merge(Term(1, PM, 2, AM), Term(0, AM, 1, AM)).Term);
    }

    [Fact]
    public void TermsWithAGapStaySeparate()
    {
        var (term, merged) = Merge(Term(1, PM, 2, PM), Term(0, AM, 0, PM));
        Assert.Equal(Term(1, PM, 2, PM), term);
        Assert.Empty(merged);

        Assert.Empty(Merge(Term(2, AM, 2, PM), Term(0, AM, 1, AM)).Merged);
    }

    [Fact]
    public void ANewTermThatBridgesTwoOthersAbsorbsBoth()
    {
        var (term, merged) = Merge(Term(2, AM, 3, PM), Term(0, AM, 1, PM), Term(4, AM, 4, PM));

        Assert.Equal(Term(0, AM, 4, PM), term);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void ATermInsideAnotherKeepsTheOuterTerm() =>
        Assert.Equal(Term(0, AM, 4, PM), Merge(Term(1, AM, 2, PM), Term(0, AM, 4, PM)).Term);

    [Fact]
    public void AnEndBeforeTheStartIsInvalid()
    {
        Assert.True(TimeOffRules.IsValid(Term(2, PM, 2, PM)));
        Assert.True(TimeOffRules.IsValid(Term(2, AM, 2, AM)));
        Assert.False(TimeOffRules.IsValid(Term(2, PM, 2, AM)));
        Assert.False(TimeOffRules.IsValid(Term(3, AM, 2, PM)));
    }
}
