using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Dev;

// Demo data for local browser checks and the Test Instance. Runs on an empty database only:
// in Development with "--seed", or anywhere when the setting Seed:PlannerEmail is set (only the Test Instance sets it).
public class DevSeed(HeadroomDbContext db, TimeProvider time, ILogger<DevSeed> log)
{
    public async Task<bool> RunAsync(string environment, string? plannerEmail, CancellationToken ct = default)
    {
        if (environment != Environments.Development && string.IsNullOrEmpty(plannerEmail))
        {
            log.LogWarning("Seed skipped: the environment is not Development and Seed:PlannerEmail is not set.");
            return false;
        }
        if (await db.InstanceSettings.AnyAsync(ct) || await db.People.AnyAsync(ct))
        {
            log.LogWarning("Seed skipped: the database is not empty.");
            return false;
        }

        var today = BrusselsTime.Today(time);
        var month = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        if (string.IsNullOrEmpty(plannerEmail)) plannerEmail = "planner@example.test";
        var planner = NewPerson("Pat Planner", plannerEmail, true);
        var person = NewPerson("Pia Person", "person@example.test", false);

        // 3 Clients, 7 Projects, 2 of them Tentative.
        var acme = new Data.Client { Name = "Acme" };
        var brightside = new Data.Client { Name = "Brightside" };
        var cobalt = new Data.Client { Name = "Cobalt" };
        var website = NewProject(acme, "Website", ProjectStatus.Confirmed);
        var mobile = NewProject(acme, "Mobile app", ProjectStatus.Tentative);
        Project[] projects =
        [
            website, mobile,
            NewProject(brightside, "Portal", ProjectStatus.Confirmed),
            NewProject(brightside, "Migration", ProjectStatus.Confirmed),
            NewProject(cobalt, "Rebrand", ProjectStatus.Tentative),
            NewProject(cobalt, "Dashboard", ProjectStatus.Confirmed),
            NewProject(cobalt, "Support", ProjectStatus.Confirmed),
        ];

        // Pat and Pia keep their first allocations. The other People come from this table.
        var team = new[] { "Ava Janssens", "Ben Peeters", "Carla Maes", "Dirk Jacobs", "Eva Willems", "Finn Claes", "Gitte Goossens", "Hugo Wouters", "Ines Mertens", "Joris Dubois", "Kim Lambert", "Lars Smets" }
            .Select(name => NewPerson(name, $"{name.Split(' ')[0].ToLowerInvariant()}@example.test", false)).ToArray();
        var partTime = team[4]; // Eva: 8, 4, 0, 8, 4 hours

        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Demo BV" });
        Person[] everyone = [planner, person, .. team];
        db.People.AddRange(everyone);
        db.Contracts.AddRange(everyone.Select(p => NewContract(p, month.AddMonths(-1), p == partTime)));
        db.Clients.AddRange(acme, brightside, cobalt);
        db.Projects.AddRange(projects);

        var overDay = Weekday(month.AddDays(7));
        db.Allocations.AddRange(
            NewAllocation(person, website, month, monthEnd, 60),
            NewAllocation(person, mobile, overDay, overDay, 60), // 120% on this day
            NewAllocation(planner, website, month, monthEnd, 40));

        // Three blocks of 4 weeks from the start of this month: about 3 months. Each Person works on two Projects.
        // Every fourth Person has a second Project at the same time in block 2: 70% + 60% is over 100%.
        int[] percentages = [50, 30, 60, 40, 70];
        for (var i = 0; i < team.Length; i++)
            for (var block = 0; block < 3; block++)
            {
                var start = month.AddDays(28 * block);
                var end = start.AddDays(27);
                var main = projects[(i + block) % projects.Length];
                if (i % 4 == 0 && block == 1)
                    db.Allocations.AddRange(
                        NewAllocation(team[i], main, Weekday(start), end, 70),
                        NewAllocation(team[i], projects[(i + 3) % projects.Length], Weekday(start), end, 60));
                else
                {
                    db.Allocations.Add(NewAllocation(team[i], main, Weekday(start), end, percentages[(i + block) % percentages.Length]));
                    if (i % 3 == 1)
                        db.Allocations.Add(NewAllocation(team[i], projects[(i + block + 2) % projects.Length], Weekday(start), end, 30));
                }
            }

        var timeOffDay = Weekday(month.AddDays(14));
        db.TimeOff.AddRange(
            NewTimeOff(person, timeOffDay, DayPart.Morning, timeOffDay, DayPart.Afternoon),
            NewTimeOff(team[1], Weekday(month.AddDays(21)), DayPart.Morning, Weekday(month.AddDays(25)), DayPart.Afternoon),
            NewTimeOff(team[5], Weekday(month.AddDays(35)), DayPart.Morning, Weekday(month.AddDays(37)), DayPart.Afternoon),
            NewTimeOff(team[8], Weekday(month.AddDays(49)), DayPart.Morning, Weekday(month.AddDays(49)), DayPart.Morning)); // a half day
        db.CompanyDaysOff.Add(new CompanyDayOff { Date = Weekday(month.AddDays(20)), Name = "Team day" });
        await db.SaveChangesAsync(ct);
        log.LogWarning("Seeded demo data. Log in as the Planner from Seed:PlannerEmail or as person@example.test.");
        return true;
    }

    private static Person NewPerson(string name, string email, bool planner) =>
        new() { Name = name, Email = email, IsPlanner = planner };

    private static Contract NewContract(Person p, DateOnly start, bool partTime) => new()
    {
        PersonId = p.Id, StartDate = start,
        HoursMonday = 8, HoursTuesday = partTime ? 4 : 8, HoursWednesday = partTime ? 0 : 8, HoursThursday = 8, HoursFriday = partTime ? 4 : 8,
    };

    private static Project NewProject(Data.Client client, string name, ProjectStatus status) =>
        new() { ClientId = client.Id, Name = name, Status = status };

    private static TimeOff NewTimeOff(Person p, DateOnly start, DayPart startPart, DateOnly end, DayPart endPart) =>
        new() { PersonId = p.Id, StartDate = start, StartPart = startPart, EndDate = end, EndPart = endPart };

    private static Allocation NewAllocation(Person p, Project project, DateOnly start, DateOnly end, int percentage) =>
        new() { PersonId = p.Id, ProjectId = project.Id, StartDate = start, EndDate = end, Percentage = percentage };

    private static DateOnly Weekday(DateOnly d) =>
        d.DayOfWeek switch { DayOfWeek.Saturday => d.AddDays(2), DayOfWeek.Sunday => d.AddDays(1), _ => d };
}
