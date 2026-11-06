using Headroom.Client.Api;

namespace Headroom.Tests.Shell;

public class AccessTokenExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("30d", "2026-12-02")]
    [InlineData("90d", "2027-01-31")]
    [InlineData("1y", "2027-11-02")]
    public void AChoiceGivesTheExpiryMoment(string choice, string date)
    {
        Assert.True(AccessTokenExpiry.TryExpiresAt(choice, Now, out var expiresAt));
        Assert.Equal(DateTimeOffset.Parse(date + "T09:00:00+00:00"), expiresAt);
    }

    [Fact]
    public void NeverGivesNoExpiryMoment()
    {
        Assert.True(AccessTokenExpiry.TryExpiresAt("never", Now, out var expiresAt));
        Assert.Null(expiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("7d")]
    [InlineData("30D")]
    [InlineData(null)]
    public void AnyOtherChoiceIsRefused(string? choice) => Assert.False(AccessTokenExpiry.TryExpiresAt(choice, Now, out _));

    [Fact]
    public void TheDefaultChoiceIs90Days() => Assert.Equal("90d", AccessTokenExpiry.DefaultChoice);

    [Theory]
    [InlineData(null, "Never", false)]
    [InlineData("2026-11-02T09:00:00+00:00", "Expired", false)]
    [InlineData("2026-11-01T09:00:00+00:00", "Expired", false)]
    [InlineData("2026-11-02T09:00:01+00:00", "02/11/2026", true)]
    [InlineData("2026-11-09T09:00:00+00:00", "09/11/2026", true)]
    [InlineData("2026-11-09T09:00:01+00:00", "09/11/2026", false)]
    public void TheTextAndTheSoonMarkFollowTheSevenDayWindow(string? expiresAt, string text, bool soon)
    {
        var moment = expiresAt is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(expiresAt);

        Assert.Equal(text, AccessTokenExpiry.Text(moment, Now));
        Assert.Equal(soon, AccessTokenExpiry.ExpiresSoon(moment, Now));
    }
}
