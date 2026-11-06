using System.Text.RegularExpressions;
using Headroom.Client.Shell;

namespace Headroom.Tests.Architecture;

public partial class UserGuideRuleTests
{
    private static readonly string Guide = Path.Combine(Repo.Root, "docs", "guides", "user-guide");

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    // Relative links of one Markdown file: (link as written, target path without anchor).
    private static IEnumerable<(string Link, string Target)> Links(string file) =>
        Link().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value)
            .Where(l => !l.StartsWith('#') && !Regex.IsMatch(l, "^[a-z]+:"))
            .Select(l => (l, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, l.Split('#')[0]))));

    [Fact]
    public void No_link_in_the_user_guide_leaves_the_folder()
    {
        var hits = Directory.EnumerateFiles(Guide, "*.md", SearchOption.AllDirectories)
            .SelectMany(f => Links(f).Where(l => !l.Target.StartsWith(Guide + Path.DirectorySeparatorChar))
                .Select(l => $"{Path.GetRelativePath(Repo.Root, f)}: {l.Link}")).ToList();
        Assert.True(hits.Count == 0, "A user guide link leaves the folder:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Every_screen_in_the_side_drawer_has_a_topic_file()
    {
        var missing = NavItem.Topics.Where(t => !File.Exists(Path.Combine(Guide, t + ".md"))).ToList();
        Assert.True(missing.Count == 0, "A screen has no topic file:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_link_in_the_user_guide_README_points_to_an_existing_file()
    {
        var hits = Links(Path.Combine(Guide, "README.md")).Where(l => !File.Exists(l.Target)).Select(l => l.Link).ToList();
        Assert.True(hits.Count == 0, "The README links to a missing file:\n" + string.Join("\n", hits));
    }
}
