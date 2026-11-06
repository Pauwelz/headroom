using Headroom.Auth;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.People;

// Erase personal details. Records of the Person stay under "Former Person N".
public static class PersonEraser
{
    // The automatic erase runs 12 months after the end date. The period is fixed.
    public const int RetentionMonths = 12;

    // The caller passes the Planner, or null for the automatic erase.
    public static async Task EraseAsync(HeadroomDbContext db, Person person, Guid? by)
    {
        // The counter lives in the Instance settings, so a number is never reused, even after a Delete.
        var settings = await db.InstanceSettings.SingleAsync();
        settings.ErasedCount++;
        person.ErasedNumber = settings.ErasedCount;
        person.Name = "";
        person.Email = null;
        person.IsPlanner = false;
        person.LastChangedBy = by;
        await db.AccessTokens.Where(t => t.PersonId == person.Id).ExecuteDeleteAsync();
        await Connections.DeleteOfPersonAsync(db, person.Id);
        await db.SaveChangesAsync();
    }

    public static async Task EraseDueAsync(HeadroomDbContext db, DateOnly today)
    {
        var limit = today.AddMonths(-RetentionMonths);
        var due = await db.People.Where(p => p.ErasedNumber == null && p.EndDate <= limit).OrderBy(p => p.EndDate).ToListAsync();
        foreach (var person in due) await EraseAsync(db, person, null);
    }
}
