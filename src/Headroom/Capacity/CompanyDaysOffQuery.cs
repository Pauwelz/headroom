using Headroom.Data;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Capacity;

public static class CompanyDaysOffQuery
{
    /// <summary>The Company days off from <paramref name="from"/> to <paramref name="to"/>, both inclusive.
    /// Pass the result to <see cref="CapacityRules"/>.</summary>
    public static async Task<IReadOnlySet<DateOnly>> CompanyDaysOffAsync(
        this HeadroomDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        (await db.CompanyDaysOff.AsNoTracking().Where(d => d.Date >= from && d.Date <= to).Select(d => d.Date).ToListAsync(ct))
        .ToHashSet();
}
