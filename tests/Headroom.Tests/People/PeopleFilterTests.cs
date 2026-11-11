using Headroom.Client.Api;
using Headroom.Client.People;

namespace Headroom.Tests.People;

public class PeopleFilterTests
{
    private static PersonResponse Person(string name, string? email, string status) =>
        new("Person", Guid.NewGuid(), name, email, false, null, status, true, 40, new PersonLocks(null, null, null, null), [], null, default);

    private static readonly PersonResponse[] Everyone =
    [
        Person("Zed", "zed@example.com", PersonStatus.Active),
        Person("Ann", "ann@work.example", PersonStatus.Leaving),
        Person("Bob", "bob@example.com", PersonStatus.Left),
        Person("Cy", "cy@example.com", PersonStatus.StartsLater),
    ];

    [Fact]
    public void CurrentHidesLeftAndSortsByName() =>
        Assert.Equal(["Ann", "Cy", "Zed"], PeopleFilter.Apply(Everyone, null, PeopleScope.Current).Select(p => p.Name));

    [Fact]
    public void LeftShowsLeft() =>
        Assert.Equal(["Bob"], PeopleFilter.Apply(Everyone, null, PeopleScope.Left).Select(p => p.Name));

    [Fact]
    public void AllShowsEveryone() =>
        Assert.Equal(4, PeopleFilter.Apply(Everyone, "", PeopleScope.All).Count());

    [Theory]
    [InlineData("WORK.exa", "Ann")]
    [InlineData("zed", "Zed")]
    public void SearchMatchesNameOrEmailWithoutCase(string search, string expected) =>
        Assert.Equal(expected, Assert.Single(PeopleFilter.Apply(Everyone, search, PeopleScope.All)).Name);
}
