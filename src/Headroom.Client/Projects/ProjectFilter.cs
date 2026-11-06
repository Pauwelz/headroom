using Headroom.Client.Api;

namespace Headroom.Client.Projects;

public enum StatusFilter { Active, Tentative, Confirmed, Archived, All }

// A Client with the Projects that pass the filter. Total counts all Projects of the Client.
public record ClientGroup(ClientResponse Client, IReadOnlyList<ProjectResponse> Projects, int Total);

/// <summary>What the Projects page filters on. A null EndedBefore keeps ended Projects. A null OnlyIn keeps every Project.</summary>
public record ProjectFilterOptions(
    string Search = "", StatusFilter Status = StatusFilter.Active, DateOnly? HideEndedBefore = null, ISet<Guid>? OnlyIn = null);

// The filter rules of the Projects page. Clients arrive sorted by name.
public static class ProjectFilter
{
    public static List<ClientGroup> Apply(IEnumerable<ClientResponse> clients, ProjectFilterOptions filter)
    {
        var (_, status, hideEndedBefore, onlyIn) = filter;
        var search = filter.Search.Trim();
        // Active is the default, so only a search or a narrower status counts as a filter.
        var filtering = search.Length > 0 || status is not (StatusFilter.Active or StatusFilter.All) || onlyIn is not null;
        var groups = new List<ClientGroup>();
        foreach (var client in clients)
        {
            var clientMatches = Contains(client.Name, search);
            var projects = client.Projects
                .Where(p => MatchesStatus(p.Status, status)
                    && !(hideEndedBefore is { } today && IsEnded(p, today))
                    && (onlyIn is null || onlyIn.Contains(p.Id)) && (clientMatches || Contains(p.Name, search)))
                .ToList();
            if (projects.Count > 0 || (client.Projects.Count == 0 && !filtering))
                groups.Add(new ClientGroup(client, projects, client.Projects.Count));
        }
        return groups;
    }

    // Ended: the last Allocation is before today. No Allocation means not ended.
    public static bool IsEnded(ProjectResponse p, DateOnly today) => p.EndDate is { } end && end < today;

    private static bool Contains(string text, string search) =>
        text.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesStatus(string projectStatus, StatusFilter filter) => filter switch
    {
        StatusFilter.Active => projectStatus != ProjectStatusName.Archived,
        StatusFilter.Tentative => projectStatus == ProjectStatusName.Tentative,
        StatusFilter.Confirmed => projectStatus == ProjectStatusName.Confirmed,
        StatusFilter.Archived => projectStatus == ProjectStatusName.Archived,
        _ => true,
    };
}
