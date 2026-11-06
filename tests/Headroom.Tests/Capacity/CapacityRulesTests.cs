using Headroom.Capacity;

namespace Headroom.Tests.Capacity;

// Worked examples E1, E2, E3, E4, E6, E8 and E9.
public class CapacityRulesTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly DateOnly Tue = Mon.AddDays(1);
    private static readonly DateOnly Wed = Mon.AddDays(2);
    private static readonly DateOnly Thu = Mon.AddDays(3);
    private static readonly DateOnly Fri = Mon.AddDays(4);

    private static readonly DateOnly Origin = new(2026, 1, 1);
    private static readonly ContractTerm ContractA = Contract(Origin, 8, 8, 8, 8, 8);
    private static readonly IReadOnlySet<DateOnly> NoDays = new HashSet<DateOnly>();

    private sealed class Holidays(params DateOnly[] days) : IPublicHolidays
    {
        public bool IsPublicHoliday(DateOnly day) => days.Contains(day);
        public IReadOnlyDictionary<DateOnly, string> InYear(int year) => days.Where(d => d.Year == year).ToDictionary(d => d, _ => "Holiday");
    }

    private static ContractTerm Contract(DateOnly start, params decimal[] monToFri) =>
        new(start, new Dictionary<DayOfWeek, decimal>
        {
            [DayOfWeek.Monday] = monToFri[0],
            [DayOfWeek.Tuesday] = monToFri[1],
            [DayOfWeek.Wednesday] = monToFri[2],
            [DayOfWeek.Thursday] = monToFri[3],
            [DayOfWeek.Friday] = monToFri[4],
        });

    private static decimal Cap(DateOnly day, ContractTerm[] contracts, IPublicHolidays? holidays = null,
        IReadOnlySet<DateOnly>? companyDaysOff = null, params TimeOffTerm[] timeOff) =>
        Day(day, contracts, holidays, companyDaysOff, timeOff).Capacity;

    private static decimal Hours(DateOnly day, int percentage, ContractTerm[] contracts, IPublicHolidays? holidays = null,
        IReadOnlySet<DateOnly>? companyDaysOff = null, params TimeOffTerm[] timeOff) =>
        Day(day, contracts, holidays, companyDaysOff, timeOff).AllocatedHours(percentage);

    private static PersonDay Day(DateOnly day, ContractTerm[] contracts, IPublicHolidays? holidays, IReadOnlySet<DateOnly>? companyDaysOff,
        TimeOffTerm[] timeOff) =>
        CapacityRules.Day(day, new PersonRecords(contracts, holidays ?? new Holidays(), companyDaysOff ?? NoDays, timeOff));

    [Fact]
    public void A_day_with_several_reasons_is_one_day_off_holiday_first_then_Company_day_then_Time_off()
    {
        var timeOff = new TimeOffTerm(Wed, DayPart.Morning, Wed, DayPart.Afternoon);
        var company = new HashSet<DateOnly> { Wed };

        var both = Day(Wed, [ContractA], new Holidays(Wed), company, [timeOff]);
        var companyAndTimeOff = Day(Wed, [ContractA], null, company, [timeOff]);
        var timeOffOnly = Day(Wed, [ContractA], null, null, [timeOff]);

        Assert.Equal((8m, 0m, 0m, 0m), (both.PublicHolidayHours, both.CompanyDayOffHours, both.TimeOffHours, both.Capacity));
        Assert.Equal((0m, 8m, 0m), (companyAndTimeOff.PublicHolidayHours, companyAndTimeOff.CompanyDayOffHours, companyAndTimeOff.TimeOffHours));
        Assert.Equal((0m, 0m, 8m), (timeOffOnly.PublicHolidayHours, timeOffOnly.CompanyDayOffHours, timeOffOnly.TimeOffHours));
    }

    [Fact]
    public void E1_half_day_of_time_off_halves_Capacity_and_keeps_allocated_hours()
    {
        var timeOff = new TimeOffTerm(Wed, DayPart.Afternoon, Wed, DayPart.Afternoon);

        var capacity = Cap(Wed, [ContractA], timeOff: timeOff);
        var hours = Hours(Wed, 100, [ContractA], timeOff: timeOff);

        Assert.Equal(4m, capacity);
        Assert.Equal(8m, hours);
        Assert.True(CapacityRules.IsOverAllocated(hours, capacity));
    }

    [Fact]
    public void E2_public_holiday_on_a_non_contract_day_removes_nothing_more()
    {
        var contractB = Contract(Origin, 8, 8, 0, 8, 8);
        var holidays = new Holidays(Wed);

        var week = new[] { Mon, Tue, Wed, Thu, Fri }.Sum(d => Cap(d, [contractB], holidays));

        Assert.Equal(32m, week);
    }

    [Fact]
    public void E3_a_day_with_several_reasons_is_one_day_off()
    {
        var holidays = new Holidays(Mon);
        var companyDaysOff = new HashSet<DateOnly> { Mon };
        var timeOff = new TimeOffTerm(Mon, DayPart.Morning, Mon, DayPart.Afternoon);

        var monday = Cap(Mon, [ContractA], holidays, companyDaysOff, timeOff);
        var week = new[] { Mon, Tue, Wed, Thu, Fri }.Sum(d => Cap(d, [ContractA], holidays, companyDaysOff, timeOff));

        Assert.Equal(0m, monday);
        Assert.Equal(32m, week);
    }

    [Fact]
    public void E4_Allocation_hours_follow_the_Contract_valid_on_each_day()
    {
        var week2Monday = Mon.AddDays(7);
        ContractTerm[] contracts = [ContractA, Contract(week2Monday, 4, 4, 4, 4, 4)];

        var week1 = new[] { Mon, Tue, Wed, Thu, Fri }.Select(d => Hours(d, 50, contracts));
        var week2 = new[] { 0, 1, 2, 3, 4 }.Select(i => Hours(week2Monday.AddDays(i), 50, contracts));

        Assert.All(week1, h => Assert.Equal(4m, h));
        Assert.All(week2, h => Assert.Equal(2m, h));
    }

    [Fact]
    public void E6_time_off_from_Wednesday_afternoon_to_Friday_morning()
    {
        var timeOff = new TimeOffTerm(Wed, DayPart.Afternoon, Fri, DayPart.Morning);

        Assert.Equal(4m, Cap(Wed, [ContractA], timeOff: timeOff));
        Assert.Equal(0m, Cap(Thu, [ContractA], timeOff: timeOff));
        Assert.Equal(4m, Cap(Fri, [ContractA], timeOff: timeOff));
    }

    [Fact]
    public void E8_Allocations_on_two_Projects_add_up_and_over_allocate()
    {
        var total = Hours(Mon, 60, [ContractA]) + Hours(Mon, 50, [ContractA]);
        var capacity = Cap(Mon, [ContractA]);

        Assert.Equal(8.8m, total);
        Assert.Equal(8m, capacity);
        Assert.True(CapacityRules.IsOverAllocated(total, capacity));
    }

    [Fact]
    public void E9_group_Utilisation_is_sum_of_hours_over_sum_of_Capacity()
    {
        var utilisation = CapacityRules.Utilisation(30m + 20m, 40m + 20m);

        Assert.Equal(0.83m, Math.Round(utilisation!.Value, 2));
    }

    [Fact]
    public void Utilisation_is_null_when_there_is_no_Capacity() =>
        Assert.Null(CapacityRules.Utilisation(0m, 0m));

    [Fact]
    public void Capacity_is_0_before_the_first_Contract_and_on_non_contract_days()
    {
        var later = Contract(Mon.AddDays(7), 8, 8, 8, 8, 8);

        Assert.Equal(0m, Cap(Mon, [later]));
        Assert.Equal(0m, Hours(Mon, 100, [later]));
        Assert.Equal(0m, Cap(Mon.AddDays(5), [ContractA]));
    }

    [Fact]
    public void A_full_day_of_time_off_gives_0_Allocation_hours()
    {
        var timeOff = new TimeOffTerm(Tue, DayPart.Morning, Tue, DayPart.Afternoon);

        Assert.Equal(0m, Hours(Tue, 100, [ContractA], timeOff: timeOff));
    }

    [Fact]
    public void Belgian_public_holidays_for_2025_2026_and_2027()
    {
        var holidays = new BelgianPublicHolidays();
        DateOnly[] expected =
        [
            new(2025, 1, 1), new(2025, 4, 21), new(2025, 5, 1), new(2025, 5, 29), new(2025, 6, 9),
            new(2025, 7, 21), new(2025, 8, 15), new(2025, 11, 1), new(2025, 11, 11), new(2025, 12, 25),
            new(2026, 1, 1), new(2026, 4, 6), new(2026, 5, 1), new(2026, 5, 14), new(2026, 5, 25),
            new(2026, 7, 21), new(2026, 8, 15), new(2026, 11, 1), new(2026, 11, 11), new(2026, 12, 25),
            new(2027, 1, 1), new(2027, 3, 29), new(2027, 5, 1), new(2027, 5, 6), new(2027, 5, 17),
            new(2027, 7, 21), new(2027, 8, 15), new(2027, 11, 1), new(2027, 11, 11), new(2027, 12, 25),
        ];

        var actual = Enumerable.Range(0, 3 * 365 + 1)
            .Select(i => new DateOnly(2025, 1, 1).AddDays(i))
            .Where(d => d.Year <= 2027 && holidays.IsPublicHoliday(d));

        Assert.Equal(expected, actual);
    }
}
