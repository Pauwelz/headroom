using Headroom.Client.Shell;

namespace Headroom.Tests.Shell;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0+5f3c2a1abcdef", "Headroom 0.1.0 (5f3c2a1)")]
    [InlineData("0.0.0-dev", "Headroom 0.0.0-dev")]
    [InlineData(null, "Headroom")]
    public void FormatShowsTheVersionAndTheShortCommit(string? input, string expected) =>
        Assert.Equal(expected, AppVersion.Format(input));
}
