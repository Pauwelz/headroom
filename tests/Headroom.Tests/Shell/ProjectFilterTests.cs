using Headroom.Client.Api;
using Headroom.Client.Projects;

namespace Headroom.Tests.Shell;

public class ProjectFilterTests
{
    private static ProjectResponse P(string name, string status) =>
        new("Project", Guid.NewGuid(), Guid.Empty, name, status, null, null, 0, default, null);

    private static ClientResponse C(string name, params ProjectResponse[] projects) =>
        new("Client", Guid.NewGuid(), name, true, projects, default, null);

    private readonly ClientResponse[] _clients =
    [
        C("Acme", P("Website", "confirmed"), P("Intranet", "tentative"), P("Old site", "archived")),
        C("Bolt", P("Brand", "archived")),
        C("Empty"),
    ];

    private IEnumerable<string> Names(string search, StatusFilter status) =>
        ProjectFilter.Apply(_clients, new(search, status)).SelectMany(g => g.Projects.Select(p => p.Name));

    [Fact]
    public void ActiveHidesArchivedProjectsAndClientsLeftWithNone() =>
        Assert.Equal(["Acme"], ProjectFilter.Apply(_clients, new("", StatusFilter.Active)).Where(g => g.Projects.Count > 0).Select(g => g.Client.Name));

    [Fact]
    public void ActiveKeepsTentativeAndConfirmed() => Assert.Equal(["Website", "Intranet"], Names("", StatusFilter.Active));

    [Theory]
    [InlineData(StatusFilter.Tentative, "Intranet")]
    [InlineData(StatusFilter.Confirmed, "Website")]
    [InlineData(StatusFilter.Archived, "Old site,Brand")]
    [InlineData(StatusFilter.All, "Website,Intranet,Old site,Brand")]
    public void StatusFilterKeepsMatchingProjects(StatusFilter status, string expected) =>
        Assert.Equal(expected.Split(','), Names("", status));

    [Fact]
    public void SearchMatchesProjectNameWithoutCase() => Assert.Equal(["Website"], Names("WEB", StatusFilter.All));

    [Fact]
    public void SearchMatchingAClientNameKeepsAllItsProjectsOfThatStatus() =>
        Assert.Equal(["Website", "Intranet"], Names("acme", StatusFilter.Active));

    [Fact]
    public void AClientWithoutProjectsShowsOnlyWhenNoFilterIsActive()
    {
        bool Shows(string search, StatusFilter status) =>
            ProjectFilter.Apply(_clients, new(search, status)).Any(g => g.Client.Name == "Empty");

        Assert.True(Shows("", StatusFilter.Active));
        Assert.True(Shows("", StatusFilter.All));
        Assert.False(Shows("e", StatusFilter.All));
        Assert.False(Shows("", StatusFilter.Archived));
    }

    private static ProjectResponse Ending(string name, DateOnly? end) =>
        new("Project", Guid.NewGuid(), Guid.Empty, name, "confirmed", end, end, end is null ? 0 : 1, default, null);

    [Fact]
    public void HideEndedKeepsProjectsWithoutAllocationsAndProjectsEndingToday()
    {
        var today = new DateOnly(2026, 3, 10);
        var past = Ending("Past", today.AddDays(-1));
        var clients = new[] { C("Acme", past, Ending("Today", today), Ending("Empty", null)) };

        Assert.Equal(["Today", "Empty"], ProjectFilter.Apply(clients, new("", StatusFilter.All, HideEndedBefore: today)).SelectMany(g => g.Projects.Select(p => p.Name)));
        Assert.Contains("Past", ProjectFilter.Apply(clients, new("", StatusFilter.All)).Single().Projects.Select(p => p.Name));
    }

    [Fact]
    public void OnlyMyProjectsKeepsListedProjectsAndHidesClientsWithoutProjects()
    {
        var mine = _clients[0].Projects[1];
        var groups = ProjectFilter.Apply(_clients, new("", StatusFilter.All, OnlyIn: new HashSet<Guid> { mine.Id }));

        Assert.Equal(["Intranet"], groups.SelectMany(g => g.Projects.Select(p => p.Name)));
        Assert.DoesNotContain(groups, g => g.Client.Name == "Empty");
    }
}
