using Headroom.Client.Api;

namespace Headroom.Tests.Architecture;

// Responses carry Type and use StartDate/EndDate, never From/To.
public class ApiVocabularyTests
{
    // Rule: a response is a public record in Headroom.Client.Api whose name ends in "Response",
    // plus every such record reachable from the properties of a response. Requests are exempt.
    private static IReadOnlyCollection<Type> ResponseTypes()
    {
        var all = typeof(DisplayDate).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace == "Headroom.Client.Api" && t.IsClass && t.GetMethod("<Clone>$") is not null)
            .ToList();
        var found = new HashSet<Type>(all.Where(t => t.Name.EndsWith("Response")));
        var queue = new Queue<Type>(found);
        while (queue.Count > 0)
            foreach (var p in queue.Dequeue().GetProperties())
                foreach (var t in Flatten(p.PropertyType).Where(all.Contains))
                    if (found.Add(t)) queue.Enqueue(t);
        return found;
    }

    private static IEnumerable<Type> Flatten(Type t) =>
        t.IsGenericType ? t.GetGenericArguments().SelectMany(Flatten) : [Nullable.GetUnderlyingType(t) ?? t];

    // Embedded value objects with no glossary name: other objects have a Type.
    private static readonly string[] NoGlossaryName = ["PersonLocks", "WeekHours"];

    [Fact]
    public void Finds_the_response_types() => Assert.True(ResponseTypes().Count >= 10);

    [Fact]
    public void Every_response_has_a_Type_property()
    {
        var missing = ResponseTypes().Where(t => t.GetProperty("Type") is null && !NoGlossaryName.Contains(t.Name)).Select(t => t.FullName).Order().ToList();
        Assert.True(missing.Count == 0, "Response types without a Type property: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_response_has_a_From_or_To_property()
    {
        var bad = ResponseTypes()
            .SelectMany(t => t.GetProperties().Where(p => p.Name is "From" or "To").Select(p => $"{t.FullName}.{p.Name}"))
            .Order().ToList();
        Assert.True(bad.Count == 0, "Use StartDate/EndDate: " + string.Join(", ", bad));
    }
}
