using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Schedule;

// Loads the plain inputs of the Schedule rules from the database.
public static class ScheduleInputs
{
    /// <summary>The Persons and Company days off the Schedule rules need for a range. The Utilisation report reuses it.</summary>
    public static async Task<(List<SchedulePersonInput> People, Dictionary<DateOnly, string> CompanyDays)> LoadAsync(
        HeadroomDbContext db, DateOnly from, DateOnly to, DateOnly today)
    {
        var people = await db.People.AsNoTracking().ToListAsync();
        var contracts = (await db.Contracts.AsNoTracking().ToListAsync()).ToLookup(c => c.PersonId);
        var allocations = await LoadAllocationsAsync(db, from, to);
        var timeOff = await LoadTimeOffAsync(db, from, to);
        var companyDays = await db.CompanyDaysOff.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to)
            .ToDictionaryAsync(d => d.Date, d => d.Name);

        var inputs = people.Select(p => new SchedulePersonInput(
            p.Id, p.Name,
            PersonStatus.Of(p.EndDate, contracts[p.Id].MinBy(c => c.StartDate)?.StartDate, today),
            p.EndDate,
            contracts[p.Id].Select(ToTerm).ToList(),
            allocations[p.Id].ToList(),
            timeOff[p.Id].ToList())).ToList();
        return (inputs, companyDays);
    }

    private static async Task<ILookup<Guid, AllocationInput>> LoadAllocationsAsync(HeadroomDbContext db, DateOnly from, DateOnly to)
    {
        var rows = await db.Allocations.AsNoTracking()
            .Where(a => a.StartDate <= to && a.EndDate >= from)
            .Join(db.Projects, a => a.ProjectId, p => p.Id, (a, p) => new { a, Project = p })
            .Join(db.Clients, x => x.Project.ClientId, c => c.Id, (x, c) => new { x.a, x.Project, ClientName = c.Name })
            .ToListAsync();
        return rows.ToLookup(r => r.a.PersonId, r => new AllocationInput(
            r.a.Id, r.Project.Id, r.Project.ClientId, r.ClientName, r.Project.Name, r.Project.StatusName,
            r.a.StartDate, r.a.EndDate, r.a.Percentage));
    }

    private static async Task<ILookup<Guid, TimeOffInput>> LoadTimeOffAsync(HeadroomDbContext db, DateOnly from, DateOnly to) =>
        (await db.TimeOff.AsNoTracking().Where(t => t.StartDate <= to && t.EndDate >= from).ToListAsync())
        .ToLookup(t => t.PersonId, t => new TimeOffInput(t.Id, t.Term));

    private static ContractTerm ToTerm(Contract c) => new(c.StartDate, new Dictionary<DayOfWeek, decimal>
    {
        [DayOfWeek.Monday] = c.HoursMonday,
        [DayOfWeek.Tuesday] = c.HoursTuesday,
        [DayOfWeek.Wednesday] = c.HoursWednesday,
        [DayOfWeek.Thursday] = c.HoursThursday,
        [DayOfWeek.Friday] = c.HoursFriday,
        [DayOfWeek.Saturday] = c.HoursSaturday,
        [DayOfWeek.Sunday] = c.HoursSunday,
    });
}
