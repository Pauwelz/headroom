using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Schedule;

namespace Headroom.Utilisation;

/// <summary>Builds the Utilisation report from plain inputs with the Capacity rules. No database.</summary>
public static class UtilisationBuilder
{
    public const string Week = "week";
    public const string Month = "month";

    /// <summary>The periods of a range. A week belongs to the range when its Thursday is inside it (ISO rule).</summary>
    public static List<UtilisationPeriod> Periods(DateOnly from, DateOnly to, string period)
    {
        var periods = new List<UtilisationPeriod>();
        if (period == Week)
        {
            for (var start = MondayOf(from); start <= to; start = start.AddDays(7))
                if (start.AddDays(3) >= from && start.AddDays(3) <= to) periods.Add(new(start, start.AddDays(6)));
        }
        else
        {
            for (var start = new DateOnly(from.Year, from.Month, 1); start <= to; start = start.AddMonths(1))
                periods.Add(new(start, start.AddMonths(1).AddDays(-1)));
        }
        return periods;
    }

    public static UtilisationResponse Build(
        DateOnly from, DateOnly to, string period, IEnumerable<SchedulePersonInput> people,
        IPublicHolidays holidays, IReadOnlyDictionary<DateOnly, string> companyDays)
    {
        var periods = Periods(from, to, period);
        var companyDaysOff = companyDays.Keys.ToHashSet();
        var rows = people
            .Where(p => ScheduleBuilder.IsShown(p, from, to))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToRow(p, from, to, periods, holidays, companyDaysOff))
            .ToList();
        // "All People" = sum of hours over sum of Capacity, never an average of percentages.
        var all = new UtilisationRow(null, "All People",
            periods.Select((_, i) => Sum(rows.Select(r => r.Cells[i]))).ToList(),
            Sum(rows.Select(r => r.Total)));
        return new UtilisationResponse("Utilisation", from, to, period, periods, all, rows);
    }

    private static UtilisationRow ToRow(
        SchedulePersonInput p, DateOnly from, DateOnly to, List<UtilisationPeriod> periods,
        IPublicHolidays holidays, IReadOnlySet<DateOnly> companyDaysOff)
    {
        var records = p.Records(holidays, companyDaysOff);
        Cell Of(DateOnly start, DateOnly end) => Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
            .Select(i => Day(p, start.AddDays(i), records))
            .Aggregate(Cell.Empty, Cell.Add);
        return new UtilisationRow(p.Id, p.Name, periods.Select(x => Of(x.StartDate, x.EndDate).ToApi()).ToList(), Of(from, to).ToApi());
    }

    private static Cell Day(SchedulePersonInput p, DateOnly day, PersonRecords records)
    {
        // After the end date the Person is gone: no contract hours, Capacity or Allocated hours.
        if (p.EndDate < day) return Cell.Empty;
        var d = CapacityRules.Day(day, records);
        var hours = p.Allocations
            .Where(a => a.StartDate <= day && a.EndDate >= day)
            .Select(a => (a, h: d.AllocatedHours(a.Percentage)))
            .Where(x => x.h > 0)
            .ToList();
        return new Cell(d.ContractHours, d.PublicHolidayHours, d.CompanyDayOffHours, d.TimeOffHours, d.Capacity,
            ByStatus(hours, ProjectStatusName.Confirmed), ByStatus(hours, ProjectStatusName.Tentative), ByStatus(hours, ProjectStatusName.Archived),
            CapacityRules.IsOverAllocated(hours.Sum(x => x.h), d.Capacity) ? 1 : 0,
            hours.Select(x => new UtilisationProject(x.a.ProjectId, x.a.ClientId, x.a.ClientName, x.a.ProjectName, x.a.ProjectStatus, x.h)).ToList());
    }

    private static decimal ByStatus(List<(AllocationInput a, decimal h)> hours, string status) =>
        hours.Where(x => x.a.ProjectStatus == status).Sum(x => x.h);

    private static UtilisationCell Sum(IEnumerable<UtilisationCell> cells) =>
        cells.Select(Cell.From).Aggregate(Cell.Empty, Cell.Add).ToApi();

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    // A running sum of days. Adding merges the Project lines, so a cell has one line per Project.
    private sealed record Cell(
        decimal Contract, decimal Holiday, decimal Company, decimal TimeOff, decimal Capacity,
        decimal Confirmed, decimal Tentative, decimal Archived, int OverDays, IReadOnlyList<UtilisationProject> Projects)
    {
        public static readonly Cell Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, []);

        public static Cell From(UtilisationCell c) => new(
            c.ContractHours, c.PublicHolidayHours, c.CompanyDayOffHours, c.TimeOffHours, c.Capacity,
            c.Confirmed, c.Tentative, c.Archived, c.OverDays, c.Projects);

        public static Cell Add(Cell a, Cell b) => new(
            a.Contract + b.Contract, a.Holiday + b.Holiday, a.Company + b.Company, a.TimeOff + b.TimeOff, a.Capacity + b.Capacity,
            a.Confirmed + b.Confirmed, a.Tentative + b.Tentative, a.Archived + b.Archived, a.OverDays + b.OverDays,
            a.Projects.Concat(b.Projects)
                .GroupBy(x => x.ProjectId)
                .Select(g => g.First() with { Hours = g.Sum(x => x.Hours) })
                .OrderBy(x => x.ClientName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList());

        public UtilisationCell ToApi() => new(
            Contract, Holiday, Company, TimeOff, Capacity, Confirmed, Tentative, Archived, OverDays,
            CapacityRules.Utilisation(Confirmed + Tentative + Archived, Capacity), Projects);
    }
}
