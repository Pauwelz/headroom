using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Schedule;
using Headroom.Utilisation;

namespace Headroom.Tests.Utilisation;

public class UtilisationBuilderTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly DateOnly Fri = Mon.AddDays(4);
    private static readonly IReadOnlyDictionary<DateOnly, string> NoCompanyDays = new Dictionary<DateOnly, string>();

    private sealed class Holidays(params DateOnly[] days) : IPublicHolidays
    {
        public bool IsPublicHoliday(DateOnly day) => days.Contains(day);
        public IReadOnlyDictionary<DateOnly, string> InYear(int year) => days.Where(d => d.Year == year).ToDictionary(d => d, _ => "Holiday");
    }

    private static ContractTerm Contract(decimal hoursMonToFri) =>
        new(new(2026, 1, 1), new Dictionary<DayOfWeek, decimal>
        {
            [DayOfWeek.Monday] = hoursMonToFri, [DayOfWeek.Tuesday] = hoursMonToFri, [DayOfWeek.Wednesday] = hoursMonToFri,
            [DayOfWeek.Thursday] = hoursMonToFri, [DayOfWeek.Friday] = hoursMonToFri,
        });

    private static AllocationInput Allocation(int percentage, string status = "confirmed", string project = "Web") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Acme", project, status, Mon, Fri, percentage);

    private static SchedulePersonInput Person(string name, decimal hours = 8, AllocationInput[]? allocations = null,
        TimeOffInput[]? timeOff = null) =>
        new(Guid.NewGuid(), name, PersonStatus.Active, null, [Contract(hours)], allocations ?? [], timeOff ?? []);

    private static UtilisationResponse Build(string period, IPublicHolidays? holidays = null,
        IReadOnlyDictionary<DateOnly, string>? companyDays = null, params SchedulePersonInput[] people) =>
        UtilisationBuilder.Build(Mon, Fri, period, people, holidays ?? new Holidays(), companyDays ?? NoCompanyDays);

    [Fact]
    public void E9_All_People_is_the_sum_of_hours_over_the_sum_of_Capacity()
    {
        // X: 30 h of 40 h (75%). Y: 20 h of 20 h (100%, 4 h a day). The average of the two is 87.5%.
        var x = Person("X", 8, [Allocation(75)]);
        var y = Person("Y", 4, [Allocation(100)]);

        var report = Build("week", null, null, x, y);

        Assert.Equal(50m / 60m, report.All.Total.Utilisation);
        Assert.Equal(60m, report.All.Total.Capacity);
        Assert.Equal(50m / 60m, report.All.Cells.Single().Utilisation);
    }

    [Fact]
    public void Confirmed_Tentative_and_Archived_Allocations_all_count()
    {
        var p = Person("Ann", 8, [Allocation(20), Allocation(30, "tentative", "App"), Allocation(10, "archived", "Old")]);

        var total = Build("week", null, null, p).People.Single().Total;

        Assert.Equal((8m, 12m, 4m), (total.Confirmed, total.Tentative, total.Archived));
        Assert.Equal(24m / 40m, total.Utilisation);
        Assert.Equal(["App", "Old", "Web"], total.Projects.Select(x => x.ProjectName));
        Assert.Equal(12m, total.Projects.Single(x => x.ProjectName == "App").Hours);
    }

    [Fact]
    public void Breakdown_names_every_hour_removed_from_the_contract_hours()
    {
        // Mon: holiday that is also a Company day (counts once). Tue: Company day. Wed: Time off afternoon. Thu: full Time off.
        var wedPm = new TimeOffInput(Guid.NewGuid(), new(Mon.AddDays(2), DayPart.Afternoon, Mon.AddDays(2), DayPart.Afternoon));
        var thu = new TimeOffInput(Guid.NewGuid(), new(Mon.AddDays(3), DayPart.Morning, Mon.AddDays(3), DayPart.Afternoon));
        var companyDays = new Dictionary<DateOnly, string> { [Mon] = "Closed", [Mon.AddDays(1)] = "Closed" };

        var total = Build("week", new Holidays(Mon), companyDays, Person("Ann", 8, timeOff: [wedPm, thu])).People.Single().Total;

        Assert.Equal(40m, total.ContractHours);
        Assert.Equal(8m, total.PublicHolidayHours);
        Assert.Equal(8m, total.CompanyDayOffHours);
        Assert.Equal(12m, total.TimeOffHours);
        Assert.Equal(12m, total.Capacity);
    }

    [Fact]
    public void Over_allocated_days_are_counted_for_the_Person_and_for_All_People()
    {
        // 60% + 50% of 8 h = 8.8 h on 8 h, on 5 days (E8).
        var ann = Person("Ann", 8, [Allocation(60), Allocation(50, project: "App")]);
        var bob = Person("Bob", 8, [Allocation(100)]);

        var report = Build("week", null, null, ann, bob);

        Assert.Equal(5, report.People[0].Total.OverDays);
        Assert.Equal(0, report.People[1].Total.OverDays);
        Assert.Equal(5, report.All.Total.OverDays);
    }

    [Fact]
    public void No_Capacity_gives_a_null_Utilisation()
    {
        var weekend = UtilisationBuilder.Build(Mon.AddDays(5), Mon.AddDays(6), "week", [Person("Ann", 8, [Allocation(50)])],
            new Holidays(), NoCompanyDays);

        Assert.Null(weekend.People.Single().Total.Utilisation);
        Assert.Null(weekend.All.Total.Utilisation);
    }

    [Fact]
    public void A_Person_without_hours_Allocation_or_Time_off_in_the_range_is_not_shown()
    {
        var report = Build("week", null, null, Person("Zed", 0), Person("Ann"), Person("Bea", 0, [Allocation(50)]));

        Assert.Equal(["Ann", "Bea"], report.People.Select(p => p.Name));
    }

    [Fact]
    public void A_week_belongs_to_the_range_when_its_Thursday_is_inside_it()
    {
        // 1 Oct 2026 is a Thursday: its week starts 28 Sep. 31 Mar 2027 is a Wednesday: the week of 29 Mar is out.
        var weeks = UtilisationBuilder.Periods(new(2026, 10, 1), new(2027, 3, 31), UtilisationBuilder.Week);

        Assert.Equal(new DateOnly(2026, 9, 28), weeks[0].StartDate);
        Assert.Equal(new DateOnly(2027, 3, 28), weeks[^1].EndDate);
        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Monday, w.StartDate.DayOfWeek));
    }

    [Fact]
    public void Months_cover_whole_calendar_months()
    {
        var months = UtilisationBuilder.Periods(new(2026, 10, 1), new(2027, 3, 31), UtilisationBuilder.Month);

        Assert.Equal(6, months.Count);
        Assert.Equal(new UtilisationPeriod(new(2027, 2, 1), new(2027, 2, 28)), months[4]);
    }
}
