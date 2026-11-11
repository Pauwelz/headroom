using Headroom.Client.Api;

namespace Headroom.Client.People;

public enum PeopleScope { Current, Left, All }

// List filter of the People page.
public static class PeopleFilter
{
    public static IEnumerable<PersonResponse> Apply(IEnumerable<PersonResponse> people, string? search, PeopleScope scope)
    {
        var text = search?.Trim() ?? "";
        return people
            .Where(p => scope switch
            {
                PeopleScope.Current => p.Status != PersonStatus.Left,
                PeopleScope.Left => p.Status == PersonStatus.Left,
                _ => true,
            })
            .Where(p => text.Length == 0
                || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }
}
