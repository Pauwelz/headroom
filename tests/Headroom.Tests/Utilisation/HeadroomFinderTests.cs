using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Schedule;
using Headroom.Utilisation;

namespace Headroom.Tests.Utilisation;

public class HeadroomFinderTests
{
    private static readonly DateOnly Mon = new(2026, 3, 2);
    private static readonly DateOnly Fri = Mon.AddDays(4);
    private static readonly IReadOnlyDictionary<DateOnly, string> NoCompanyDays = new Dictionary<DateOnly, string>();

    private sealed class NoHolidays : IPublicHolidays
    {
        public bool IsPublicHoliday(DateOnly day) => false;
        public IReadOnlyDictionary<DateOnly, string> InYear(int year) => new Dictionary<DateOnly, string>();
    }

    private static AllocationInput Allocation(int percentage, string status = "confirmed") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Acme", "Web", status, Mon, Fri, percentage);

    private static SchedulePersonInput Person(string name, AllocationInput[]? allocations = null, TimeOffInput[]? timeOff = null)
    {
        var term = new ContractTerm(new(2026, 1, 1), new Dictionary<DayOfWeek, decimal>
        {
            [DayOfWeek.Monday] = 8, [DayOfWeek.Tuesday] = 8, [DayOfWeek.Wednesday] = 8, [DayOfWeek.Thursday] = 8, [DayOfWeek.Friday] = 8,
        });
        return new(Guid.NewGuid(), name, PersonStatus.Active, null, [term], allocations ?? [], timeOff ?? []);
    }

    private static List<PersonHeadroom> Find(decimal hours, IReadOnlyDictionary<DateOnly, string>? companyDays = null, params SchedulePersonInput[] people) =>
        HeadroomFinder.Find(UtilisationBuilder.Build(Mon, Fri, UtilisationBuilder.Week, people, new NoHolidays(), companyDays ?? NoCompanyDays), hours, new HashSet<Guid>());

    [Fact]
    public void Free_hours_are_Capacity_minus_all_allocated_hours_and_Tentative_counts_like_in_the_report()
    {
        // 40 h Capacity. 50% confirmed = 20 h, 25% tentative = 10 h. 10 h are free.
        var ann = Person("Ann", [Allocation(50), Allocation(25, "tentative")]);

        var found = Assert.Single(Find(0, null, ann));

        Assert.Equal((40m, 30m, 10m), (found.Capacity, found.AllocatedHours, found.FreeHours));
    }

    [Fact]
    public void Time_off_and_a_Company_day_off_lower_the_free_hours()
    {
        // Mon is a Company day off, Tue is a full day Time off. Capacity is 24 h. 50% of 24 h = 12 h is allocated.
        var tue = new TimeOffInput(Guid.NewGuid(), new(Mon.AddDays(1), DayPart.Morning, Mon.AddDays(1), DayPart.Afternoon));
        var ann = Person("Ann", [Allocation(50)], [tue]);

        var found = Assert.Single(Find(0, new Dictionary<DateOnly, string> { [Mon] = "Bridge" }, ann));

        Assert.Equal((24m, 12m, 12m), (found.Capacity, found.AllocatedHours, found.FreeHours));
    }

    [Fact]
    public void An_Over_allocated_Person_has_no_free_hours_and_is_left_out()
    {
        var bob = Person("Bob", [Allocation(100), Allocation(50)]);

        Assert.Empty(Find(0, null, bob));
    }

    [Fact]
    public void Only_People_with_more_free_hours_than_the_minimum_are_returned_sorted_by_name()
    {
        var ann = Person("Ann", [Allocation(50)]);   // 20 h free
        var bob = Person("Bob", [Allocation(75)]);   // 10 h free
        var cy = Person("Cy");                       // 40 h free

        Assert.Equal(["Ann", "Bob", "Cy"], Find(0, null, cy, bob, ann).Select(x => x.Name));
        Assert.Equal(["Ann", "Cy"], Find(10, null, cy, bob, ann).Select(x => x.Name));
    }
}
