using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.People;

// The last Planner who can log in cannot lose the flag, get an end date, be erased or be deleted.
public static class LastPlanner
{
    public const string Message = "This is the last Planner who can log in. Make another Person a Planner first.";

    public static async Task<bool> IsAsync(HeadroomDbContext db, Person person, DateOnly today) =>
        person.IsPlanner && person.CanLogIn(today)
        && !await db.People.AnyAsync(p => p.Id != person.Id && p.IsPlanner && (p.EndDate == null || p.EndDate >= today));
}
