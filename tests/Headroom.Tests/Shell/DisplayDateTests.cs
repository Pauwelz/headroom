using System.Globalization;
using Headroom.Client.Api;

namespace Headroom.Tests.Shell;

public class DisplayDateTests
{
    [Fact]
    public void ADateShowsAsDdMmYyyyInAnyCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try { Assert.Equal("02/11/2026", DisplayDate.Format(new DateOnly(2026, 11, 2))); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void ANullDateShowsTheFallback() => Assert.Equal("open", DisplayDate.Format(null, "open"));
}
