using Headroom.Client.Shell;

namespace Headroom.Tests.Shell;

public class AutocompleteSearchTests
{
    private static readonly string[] Names = ["Alice", "Bob", "Malice"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyTextShowsAllItems(string? text) =>
        Assert.Equal(Names, AutocompleteSearch.Match(Names, text, n => n));

    [Fact]
    public void TheMatchIsCaseInsensitiveAndContains() =>
        Assert.Equal(["Alice", "Malice"], AutocompleteSearch.Match(Names, "LIC", n => n));

    [Fact]
    public void NoMatchGivesNoItems() =>
        Assert.Empty(AutocompleteSearch.Match(Names, "zzz", n => n));

    [Fact]
    public async Task TheSearchFunctionUsesTheSameMatch()
    {
        var search = AutocompleteSearch.For(Names, n => n);

        Assert.Equal(["Bob"], await search("bo", CancellationToken.None));
    }
}
