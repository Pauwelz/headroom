using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Client.Schedule;
using Headroom.Schedule;
using Microsoft.Extensions.Time.Testing;

namespace Headroom.Tests.Schedule;

public class ScheduleBuilderTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly DateOnly Fri = Mon.AddDays(4);
    private static readonly ContractTerm FullTime = Contract(new(2026, 1, 1), 8);
    private static readonly IReadOnlyDictionary<DateOnly, string> NoCompanyDays = new Dictionary<DateOnly, string>();

    private sealed class Holidays(params DateOnly[] days) : IPublicHolidays
    {
        public bool IsPublicHoliday(DateOnly day) => days.Contains(day);
        public IReadOnlyDictionary<DateOnly, string> InYear(int year) => days.Where(d => d.Year == year).ToDictionary(d => d, _ => "Holiday");
    }

    private static ContractTerm Contract(DateOnly start, decimal hoursMonToFri) =>
        new(start, new Dictionary<DayOfWeek, decimal>
        {
            [DayOfWeek.Monday] = hoursMonToFri, [DayOfWeek.Tuesday] = hoursMonToFri, [DayOfWeek.Wednesday] = hoursMonToFri,
            [DayOfWeek.Thursday] = hoursMonToFri, [DayOfWeek.Friday] = hoursMonToFri,
        });

    private static AllocationInput Allocation(int percentage, string client = "Acme", string project = "Web",
        string status = "confirmed", DateOnly? start = null, DateOnly? end = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), client, project, status, start ?? Mon, end ?? Fri, percentage);

    private static SchedulePersonInput Person(string name, ContractTerm[]? contracts = null, AllocationInput[]? allocations = null,
        TimeOffInput[]? timeOff = null, DateOnly? end = null) =>
        new(Guid.NewGuid(), name, PersonStatus.Active, end, contracts ?? [FullTime], allocations ?? [], timeOff ?? []);

    private static ScheduleResponse Build(DateOnly from, DateOnly to, IPublicHolidays? holidays = null,
        IReadOnlyDictionary<DateOnly, string>? companyDays = null, params SchedulePersonInput[] people) =>
        ScheduleBuilder.Build(from, to, people, holidays ?? new Holidays(), companyDays ?? NoCompanyDays);

    private static SchedulePerson One(ScheduleResponse r) => Assert.Single(r.People);

    [Fact]
    public void E1_half_day_of_Time_off_makes_the_day_over_allocated()
    {
        var wedPm = new TimeOffInput(Guid.NewGuid(), new(Mon.AddDays(2), DayPart.Afternoon, Mon.AddDays(2), DayPart.Afternoon));
        var person = One(Build(Mon, Fri, null, null, Person("Ann", allocations: [Allocation(100)], timeOff: [wedPm])));

        var wed = person.Days[2];
        Assert.Equal(4, wed.Capacity);
        Assert.Equal(8, wed.Hours);
        Assert.Equal(2m, wed.Utilisation);
    }

    [Fact]
    public void Load_is_hours_divided_by_Capacity_and_two_Allocations_add_up()
    {
        var person = One(Build(Mon, Fri, null, null, Person("Ann", allocations: [Allocation(60), Allocation(50, project: "App")])));

        Assert.All(person.Days, d => Assert.Equal(8.8m, d.Hours));
        Assert.All(person.Days, d => Assert.Equal(1.1m, d.Utilisation));
    }

    [Fact]
    public void A_day_without_Capacity_has_no_load()
    {
        var holiday = Mon.AddDays(1);
        var response = Build(Mon, Mon.AddDays(6), new Holidays(holiday), null, Person("Ann", allocations: [Allocation(100, end: Mon.AddDays(6))]));

        var days = One(response).Days;
        Assert.Null(days[1].Utilisation); // public holiday
        Assert.Null(days[5].Utilisation); // Saturday
        Assert.Equal(1m, days[0].Utilisation);
        Assert.Equal(0, days[1].Hours);
    }

    [Fact]
    public void Public_holidays_and_Company_days_off_are_listed_with_their_names()
    {
        var company = new Dictionary<DateOnly, string> { [Mon.AddDays(2)] = "Closure" };
        var response = Build(Mon, Fri, new Holidays(Mon), company, Person("Ann"));

        Assert.Contains(response.DaysOff, d => d.Date == Mon && d.Type == "PublicHoliday");
        Assert.Contains(response.DaysOff, d => d.Date == Mon.AddDays(2) && d is { Type: "CompanyDayOff", Name: "Closure" });
        Assert.Equal(0, One(response).Days[2].Capacity);
    }

    [Fact]
    public void People_are_sorted_by_name_without_case()
    {
        var response = Build(Mon, Fri, null, null, Person("zed"), Person("Bob"), Person("Ann"));

        Assert.Equal(["Ann", "Bob", "zed"], response.People.Select(p => p.Name));
    }

    [Fact]
    public void A_Person_shows_for_a_Contract_with_hours_an_Allocation_or_Time_off_in_the_range()
    {
        var later = Contract(Mon.AddDays(30), 8);
        var zero = Contract(new(2026, 1, 1), 0);
        var timeOff = new TimeOffInput(Guid.NewGuid(), new(Mon, DayPart.Morning, Mon, DayPart.Afternoon));
        var response = Build(Mon, Fri, null, null,
            Person("Contract"),
            Person("Starts later", [later]),
            Person("Zero hours", [zero]),
            Person("Zero hours with Allocation", [zero], [Allocation(50)]),
            Person("Zero hours with Time off", [zero], timeOff: [timeOff]),
            Person("Left before", end: Mon.AddDays(-1)),
            Person("Allocation outside", [zero], [Allocation(50, start: Mon.AddDays(10), end: Mon.AddDays(12))]));

        Assert.Equal(["Contract", "Zero hours with Allocation", "Zero hours with Time off"], response.People.Select(p => p.Name));
    }

    [Fact]
    public void Days_after_the_end_date_have_no_Capacity()
    {
        var person = One(Build(Mon, Fri, null, null, Person("Ann", end: Mon.AddDays(1))));

        Assert.Equal([8m, 8m, 0m, 0m, 0m], person.Days.Select(d => d.Capacity));
    }

    [Fact]
    public void Project_rows_are_sorted_by_Client_then_Project_and_bars_stay_on_their_row()
    {
        var second = Allocation(50, "Acme", "Web", start: Mon.AddDays(3), end: Fri);
        var first = second with { Id = Guid.NewGuid(), StartDate = Mon, EndDate = Mon.AddDays(1) };
        var other = Allocation(50, "Acme", "App");
        var beta = Allocation(50, "Beta", "A");
        var person = One(Build(Mon, Fri, null, null, Person("Ann", allocations: [beta, second, other, first])));

        Assert.Equal(["App", "Web", "A"], person.Projects.Select(p => p.ProjectName));
        Assert.Equal([Mon, Mon.AddDays(3)], person.Projects[1].Allocations.Select(a => a.StartDate));
    }

    [Fact]
    public void Time_off_bars_carry_the_day_parts()
    {
        var term = new TimeOffTerm(Mon.AddDays(2), DayPart.Afternoon, Fri, DayPart.Morning);
        var person = One(Build(Mon, Fri, null, null, Person("Ann", timeOff: [new(Guid.NewGuid(), term)])));

        var off = Assert.Single(person.TimeOff);
        Assert.Equal(("afternoon", "morning"), (off.StartPart, off.EndPart));
        Assert.Equal([8m, 8m, 4m, 0m, 4m], person.Days.Select(d => d.Capacity));
    }
}

public class ScheduleRangeTests
{
    [Fact]
    public void The_default_is_6_weeks_from_the_Monday_of_this_week()
    {
        var range = ScheduleRange.Default(new DateOnly(2026, 10, 30)); // a Friday

        Assert.Equal(new DateOnly(2026, 10, 26), range.From);
        Assert.Equal(new DateOnly(2026, 12, 6), range.To);
        Assert.Equal(42, range.Days);
    }

    [Theory]
    [InlineData(0, 2026, 11, 1, 2026, 11, 14)] // 2 weeks
    [InlineData(1, 2026, 11, 1, 2026, 12, 12)] // 6 weeks
    [InlineData(2, 2026, 11, 1, 2027, 1, 31)] // 3 months: 92 days
    [InlineData(2, 2027, 2, 1, 2027, 4, 30)] // 3 months: 89 days
    [InlineData(2, 2026, 11, 30, 2027, 2, 27)] // the month end is clamped
    public void A_preset_keeps_the_first_day(int preset, int y, int m, int d, int ty, int tm, int td)
    {
        var range = ScheduleRange.Presets[preset].Apply(new DateOnly(y, m, d));

        Assert.Equal(new ScheduleRange(new DateOnly(y, m, d), new DateOnly(ty, tm, td)), range);
    }

    [Theory]
    [InlineData(2026, 11, 1, 2027, 1, 31, null)]
    [InlineData(2026, 11, 1, 2026, 11, 1, null)] // one day
    [InlineData(2026, 11, 1, 2027, 2, 1, "Pick at most 3 months.")]
    [InlineData(2026, 11, 1, 2026, 10, 31, "The end date must be on or after the start date.")]
    public void A_range_is_valid_up_to_3_calendar_months_from_its_first_day(int y, int m, int d, int ty, int tm, int td, string? error) =>
        Assert.Equal(error, ScheduleRange.Validate(new DateOnly(y, m, d), new DateOnly(ty, tm, td)));

    [Fact]
    public void Moving_keeps_the_number_of_days_even_above_the_picker_limit()
    {
        var range = new ScheduleRange(new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31)); // 92 days

        var next = range.Move(1);
        Assert.Equal(new DateOnly(2027, 2, 1), next.From);
        Assert.Equal(92, next.Days);
        Assert.Equal(new DateOnly(2026, 11, 1).AddDays(-92), range.Move(-1).From);
        Assert.Equal(92, range.StartingAt(new DateOnly(2026, 12, 15)).Days);
    }

    [Theory]
    [InlineData(14, 56)]
    [InlineData(42, 84)]
    [InlineData(92, 134)]
    public void The_window_is_the_range_plus_3_weeks_on_each_side(int days, int window)
    {
        var range = new ScheduleRange(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2).AddDays(days - 1)); // a Monday

        Assert.Equal(window, range.WindowDays);
        Assert.Equal(new DateOnly(2026, 10, 12), range.WindowFrom(null));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(42)]
    [InlineData(92)]
    public void The_window_stays_while_a_week_of_loaded_days_remains_on_both_sides(int days)
    {
        ScheduleRange At(DateOnly from) => new(from, from.AddDays(days - 1));
        var start = At(new DateOnly(2026, 11, 2));
        var window = start.WindowFrom(null);
        var windowEnd = window.AddDays(start.WindowDays - 1);

        Assert.Equal(window, At(window.AddDays(7)).WindowFrom(window));
        Assert.Equal(window, At(windowEnd.AddDays(-7 - days + 1)).WindowFrom(window));
        Assert.NotEqual(window, At(window.AddDays(6)).WindowFrom(window)); // scrolled to the left edge
        Assert.NotEqual(window, At(windowEnd.AddDays(-6 - days + 1)).WindowFrom(window)); // scrolled to the right edge
    }

    [Fact]
    public void Dates_show_as_dd_mm_yyyy_and_the_Sunday_belongs_to_the_week_before()
    {
        Assert.Equal("05/03/2026", DisplayDate.Format(new DateOnly(2026, 3, 5)));
        Assert.Equal(new DateOnly(2026, 10, 26), ScheduleRange.MondayOf(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Today_is_the_date_in_Brussels()
    {
        var lateUtc = new FakeTimeProvider(new DateTimeOffset(2026, 6, 30, 22, 30, 0, TimeSpan.Zero)); // 00:30 on 1 July in Brussels

        Assert.Equal(new DateOnly(2026, 7, 1), ScheduleRange.TodayInBrussels(lateUtc));
    }
}

public class ScheduleGridRowsTests
{
    private static ScheduleProject Project(string name) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Acme", name, "Confirmed", [new ScheduleAllocation(Guid.NewGuid(), new(2026, 11, 2), new(2026, 11, 6), 50)]);

    [Fact]
    public void A_Project_with_no_Allocation_in_the_new_days_stays_as_an_empty_row()
    {
        var kept = Project("Website");
        var still = Project("App");
        var now = new SchedulePerson("SchedulePerson", Guid.NewGuid(), "Ann", "Active", [], [still], []);

        var rows = ScheduleGrid.EmptiedRows([kept, still], now).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(kept.ProjectId, row.ProjectId);
        Assert.Empty(row.Allocations);
        Assert.Empty(ScheduleGrid.EmptiedRows([kept], null));
    }

    [Fact]
    public void A_month_heading_is_long_or_short()
    {
        Assert.Equal("November 2026", DisplayDate.Month(new DateOnly(2026, 11, 5), false));
        Assert.Equal("Nov", DisplayDate.Month(new DateOnly(2026, 11, 5), true));
    }
}
