using Headroom.Client.Api;

namespace Headroom.Utilisation;

public record PersonHeadroom(Guid PersonId, string Name, decimal Capacity, decimal AllocatedHours, decimal FreeHours);

/// <summary>Finds People with free hours in a Utilisation report. Erased People are skipped: they have no Capacity to offer.</summary>
public static class HeadroomFinder
{
    public static List<PersonHeadroom> Find(UtilisationResponse report, decimal minimumFreeHours, IReadOnlySet<Guid> erasedIds) =>
        report.People
            .Where(p => p.PersonId is { } id && !erasedIds.Contains(id))
            // Archived counts as allocated: they are booked hours, as in Utilisation.
            .Select(p => (p, allocated: p.Total.Confirmed + p.Total.Tentative + p.Total.Archived))
            .Select(x => new PersonHeadroom(x.p.PersonId!.Value, x.p.Name, x.p.Total.Capacity, x.allocated, x.p.Total.Capacity - x.allocated))
            .Where(x => x.FreeHours > minimumFreeHours)
            .ToList();
}
