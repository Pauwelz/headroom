using Headroom.Client.Api;

namespace Headroom.Client.People;

// Text of the Employment card.
public static class EmploymentText
{
    public static string Sentence(PersonResponse p)
    {
        var first = p.Contracts.FirstOrDefault()?.StartDate;
        return p.Status switch
        {
            PersonStatus.StartsLater => $"Starts later. The first Contract starts on {DisplayDate.Format(first)}. There is no end date.",
            PersonStatus.Leaving => $"Leaving. The last working day is {DisplayDate.Format(p.EndDate)}. The login stops on {DisplayDate.Format(p.EndDate!.Value.AddDays(1))}.",
            PersonStatus.Left => $"Left. The last working day was {DisplayDate.Format(p.EndDate)}. The login stopped on {DisplayDate.Format(p.EndDate!.Value.AddDays(1))}.",
            PersonStatus.Erased => "Erased. The personal details are removed.",
            _ => "Active. There is no end date.",
        };
    }

    // A null reference shows as "System".
    public static string ChangedBy(Guid? id, IEnumerable<PersonResponse> everyone) =>
        id is null ? "System" : everyone.FirstOrDefault(p => p.Id == id)?.Name ?? "Unknown";

    // The last Contract with hours is the default in the Rehire dialog.
    public static WeekHours DefaultRehireHours(PersonResponse p) =>
        p.Contracts.LastOrDefault(c => c.Hours.Total > 0)?.Hours ?? WeekHours.FullTime;
}
