using Headroom.Auth;
using Headroom.Data;
using Headroom.Events;
using Microsoft.EntityFrameworkCore;

namespace Headroom.People;

// Delete a Person with their Contracts, Allocations, Time off, Passkeys, Access tokens and Connections.
// "Last changed by" on other records keeps the id.
public static class PersonDeleter
{
    // The daily check deletes 12 months after the end date. The period is fixed.
    public const int RetentionMonths = 12;

    public static async Task DeleteAsync(HeadroomDbContext db, Person person)
    {
        var id = person.Id;
        await using var transaction = await db.Database.BeginTransactionAsync();
        // The deleted Events read the name and email from the stored Person, so blank them in a first save.
        person.Name = "";
        person.Email = null;
        await db.SaveChangesAsync();

        await db.AccessTokens.Where(t => t.PersonId == id).ExecuteDeleteAsync();
        await db.Passkeys.Where(p => p.PersonId == id).ExecuteDeleteAsync();
        await Connections.DeleteOfPersonAsync(db, id);
        await EventRecorder.BlankPersonAsync(db, id);
        // The save records one deleted Event for each Allocation and Time off.
        db.Allocations.RemoveRange(db.Allocations.Where(a => a.PersonId == id));
        db.TimeOff.RemoveRange(db.TimeOff.Where(t => t.PersonId == id));
        db.Contracts.RemoveRange(db.Contracts.Where(c => c.PersonId == id));
        db.People.Remove(person);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public static async Task DeleteDueAsync(HeadroomDbContext db, DateOnly today)
    {
        var limit = today.AddMonths(-RetentionMonths);
        var due = await db.People.Where(p => p.EndDate <= limit).OrderBy(p => p.EndDate).ToListAsync();
        foreach (var person in due) await DeleteAsync(db, person);
    }
}
