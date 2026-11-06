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
                PeopleScope.Current => p.Status is not (PersonStatus.Left or PersonStatus.Erased),
                PeopleScope.Left => p.Status is PersonStatus.Left or PersonStatus.Erased,
                _ => true,
            })
            .Where(p => text.Length == 0
                || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }
}
