using Headroom.Capacity;
using Headroom.Client.Api;

namespace Headroom.Schedule;

public sealed record AllocationInput(
    Guid Id, Guid ProjectId, Guid ClientId, string ClientName, string ProjectName, string ProjectStatus,
    DateOnly StartDate, DateOnly EndDate, int Percentage);

public sealed record TimeOffInput(Guid Id, TimeOffTerm Term);

/// <summary>A Person with all records the Schedule needs. EndDate is the last working day.</summary>
public sealed record SchedulePersonInput(
    Guid Id, string Name, string Status, DateOnly? EndDate,
    IReadOnlyList<ContractTerm> Contracts, IReadOnlyList<AllocationInput> Allocations, IReadOnlyList<TimeOffInput> TimeOff)
{
    public PersonRecords Records(IPublicHolidays holidays, IReadOnlySet<DateOnly> companyDaysOff) =>
        new(Contracts, holidays, companyDaysOff, TimeOff.Select(t => t.Term).ToList());
}

/// <summary>Builds the Schedule response from plain inputs with the Capacity rules. No database.</summary>
public static class ScheduleBuilder
{
    public static ScheduleResponse Build(
        DateOnly from, DateOnly to, IEnumerable<SchedulePersonInput> people,
        IPublicHolidays holidays, IReadOnlyDictionary<DateOnly, string> companyDays)
    {
        var companyDaysOff = companyDays.Keys.ToHashSet();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var daysOff = days.SelectMany(d => DaysOff(d, holidays, companyDays)).ToList();
        var shown = people
            .Where(p => IsShown(p, from, to))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToPerson(p, days, holidays, companyDaysOff))
            .ToList();
        return new ScheduleResponse("Schedule", from, to, daysOff, shown);
    }

    // A Contract with hours, an Allocation or Time off falls in the range.
    public static bool IsShown(SchedulePersonInput p, DateOnly from, DateOnly to) =>
        p.Allocations.Any(a => a.StartDate <= to && a.EndDate >= from)
        || p.TimeOff.Any(t => t.Term.StartDate <= to && t.Term.EndDate >= from)
        || ContractsWithHours(p).Any(c => c.Start <= to && c.End >= from);

    // A Contract is valid until the next Contract starts, or until the end date of the Person.
    private static IEnumerable<(DateOnly Start, DateOnly End)> ContractsWithHours(SchedulePersonInput p)
    {
        var sorted = p.Contracts.OrderBy(c => c.Start).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var end = i + 1 < sorted.Count ? sorted[i + 1].Start.AddDays(-1) : DateOnly.MaxValue;
            if (p.EndDate is { } last && last < end) end = last;
            if (sorted[i].Hours.Values.Sum() > 0) yield return (sorted[i].Start, end);
        }
    }

    private static IEnumerable<ScheduleDayOff> DaysOff(
        DateOnly day, IPublicHolidays holidays, IReadOnlyDictionary<DateOnly, string> companyDays)
    {
        if (holidays.IsPublicHoliday(day))
            yield return new ScheduleDayOff(DayOffTypeName.PublicHoliday, day, holidays.InYear(day.Year).GetValueOrDefault(day, "Public holiday"));
        if (companyDays.TryGetValue(day, out var name)) yield return new ScheduleDayOff(DayOffTypeName.CompanyDayOff, day, name);
    }

    private static SchedulePerson ToPerson(
        SchedulePersonInput p, List<DateOnly> days, IPublicHolidays holidays, IReadOnlySet<DateOnly> companyDaysOff)
    {
        var records = p.Records(holidays, companyDaysOff);
        var rows = days.Select(day =>
        {
            // After the end date the Person is gone: Capacity and hours are 0.
            if (p.EndDate < day) return new ScheduleDay(day, 0, 0, null);
            var personDay = CapacityRules.Day(day, records);
            var hours = p.Allocations
                .Where(a => a.StartDate <= day && a.EndDate >= day)
                .Sum(a => personDay.AllocatedHours(a.Percentage));
            return new ScheduleDay(day, personDay.Capacity, hours, CapacityRules.Utilisation(hours, personDay.Capacity));
        }).ToList();

        var from = days[0];
        var to = days[^1];
        var projects = p.Allocations
            .Where(a => a.StartDate <= to && a.EndDate >= from)
            .GroupBy(a => a.ProjectId)
            .Select(g => (First: g.First(), Bars: g))
            .OrderBy(g => g.First.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.First.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ScheduleProject(g.First.ProjectId, g.First.ClientId, g.First.ClientName, g.First.ProjectName, g.First.ProjectStatus,
                g.Bars.OrderBy(a => a.StartDate).Select(a => new ScheduleAllocation(a.Id, a.StartDate, a.EndDate, a.Percentage)).ToList()))
            .ToList();
        var off = p.TimeOff
            .Where(t => t.Term.StartDate <= to && t.Term.EndDate >= from)
            .OrderBy(t => t.Term.StartDate)
            .Select(t => new ScheduleTimeOff(t.Id, t.Term.StartDate, DayPartNames.ToApi(t.Term.StartPart), t.Term.EndDate, DayPartNames.ToApi(t.Term.EndPart)))
            .ToList();
        return new SchedulePerson("Person", p.Id, p.Name, p.Status, rows, projects, off);
    }
}
