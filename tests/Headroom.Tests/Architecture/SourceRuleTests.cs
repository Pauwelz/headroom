using System.Text.RegularExpressions;

namespace Headroom.Tests.Architecture;

public partial class SourceRuleTests
{
    // A string literal that looks like a date format: yy, yyyy, d/M, dd/MM, MM-dd, MMM, ...
    [GeneratedRegex("\"[^\"\\n]*(?<![A-Za-z])(y{2,4}|d{1,2}[/.-]M{1,4}|M{1,2}[/.-]d{1,2}|M{3,4})(?![A-Za-z])[^\"\\n]*\"")]
    private static partial Regex DateFormat();

    [GeneratedRegex("\"(tentative|confirmed|archived)\"", RegexOptions.IgnoreCase)]
    private static partial Regex StatusLiteral();

    // The user sees dates only through DisplayDate. Other files may keep a format only for the reason given.
    private static readonly Dictionary<string, string> DateFormatAllowed = new()
    {
        ["src/Headroom.Client/Api/DisplayDate.cs"] = "the one formatter",
        ["src/Headroom.Client/People/PeopleApi.cs"] = "builds an API query (yyyy-MM-dd)",
        ["src/Headroom.Client/Schedule/ScheduleApi.cs"] = "builds an API query (yyyy-MM-dd)",
        ["src/Headroom.Client/Utilisation/UtilisationApi.cs"] = "builds an API query (yyyy-MM-dd)",
        ["src/Headroom.Client/Utilisation/UtilisationHeatmap.razor"] = "month column header (MMM yyyy), not a date",
    };

    // Status names come from these files only.
    private static readonly string[] StatusAllowed = ["src/Headroom.Client/Api/ApiNames.cs"];

    [Fact]
    public void No_hand_written_date_format_outside_the_allowlist()
    {
        var hits = Repo.Lines("Headroom.Client", ".cs", ".razor")
            .Where(l => !DateFormatAllowed.ContainsKey(l.File) && DateFormat().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}").ToList();
        Assert.True(hits.Count == 0, "Use DisplayDate:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void No_status_literal_outside_the_shared_constants()
    {
        var hits = new[] { "Headroom.Client", "Headroom" }
            .SelectMany(p => Repo.Lines(p, ".cs", ".razor"))
            .Where(l => !l.File.Contains("/Migrations/") && !StatusAllowed.Contains(l.File) && StatusLiteral().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}").ToList();
        Assert.True(hits.Count == 0, "Use ProjectStatusName or Project.StatusName:\n" + string.Join("\n", hits));
    }
}
