using Headroom.Client.Shell;

namespace Headroom.Client.Help;

public static class HelpTopics
{
    /// <summary>The help address for a route. A side drawer screen gets the topic of its first route segment. Other routes get the contents.</summary>
    public static string For(string path)
    {
        var segment = path.Split(['/', '?', '#'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return segment is not null && NavItem.Topics.Contains(segment) ? $"/help/{segment}" : "/help";
    }
}
