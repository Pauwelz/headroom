using Headroom.Client.Api;
using Headroom.Client.Settings;

namespace Headroom.Tests.Shell;

public class SettingsSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static DayOffResponse Company(int month, int day, string name) => new(DayOffTypeName.CompanyDayOff, Guid.NewGuid(), new(2026, month, day), name);
    private static DayOffResponse Holiday(int month, int day) => new(DayOffTypeName.PublicHoliday, null, new(2026, month, day), "Holiday");

    private static AccessTokenResponse Token(string owner, DateTimeOffset? expiresAt = null) =>
        new("AccessToken", Guid.NewGuid(), "Token", owner, Now.AddDays(-30), expiresAt, false);

    private static ConnectionResponse Connection(string owner) => new(Guid.NewGuid().ToString(), "Claude", owner, Now, null, false);

    [Fact]
    public void DaysOffCountsOnlyCompanyDaysOffAndNamesTheNextOne()
    {
        var days = new[] { Holiday(1, 1), Company(3, 2, "Spring day"), Company(12, 24, "Christmas Eve"), Company(11, 23, "Team day") };

        Assert.Equal("3 Company days off in 2026. Next: Team day, 23/11/2026", SettingsSummary.DaysOff(days, 2026, Today));
    }

    [Fact]
    public void DaysOffTodayIsTheNextOne() =>
        Assert.Equal("1 Company day off in 2026. Next: Bridge day, 05/11/2026", SettingsSummary.DaysOff([Company(11, 5, "Bridge day")], 2026, Today));

    [Fact]
    public void DaysOffWithoutANextOneGivesOnlyTheCount() =>
        Assert.Equal("1 Company day off in 2026", SettingsSummary.DaysOff([Company(3, 2, "Spring day")], 2026, Today));

    [Fact]
    public void DaysOffWithOnlyPublicHolidaysSaysNone() =>
        Assert.Equal("No Company days off in 2026", SettingsSummary.DaysOff([Holiday(1, 1)], 2026, Today));

    [Fact]
    public void TokensCountTheTokensAndTheirOwners()
    {
        Assert.Equal("No tokens", SettingsSummary.Tokens([]));
        Assert.Equal("1 token of 1 Person", SettingsSummary.Tokens([Token("Pia")]));
        Assert.Equal("3 tokens of 2 People", SettingsSummary.Tokens([Token("Pia"), Token("Pat"), Token("Pia")]));
    }

    [Fact]
    public void ConnectionsCountTheConnectionsAndTheirPeople()
    {
        Assert.Equal("No Connections", SettingsSummary.Connections([]));
        Assert.Equal("1 Connection of 1 Person", SettingsSummary.Connections([Connection("Pia")]));
        Assert.Equal("2 Connections of 2 People", SettingsSummary.Connections([Connection("Pia"), Connection("Pat")]));
    }

    [Fact]
    public void NamesJoinTheNamesOrSayNone()
    {
        Assert.Equal("None", SettingsSummary.Names([]));
        Assert.Equal("Claude, Copilot Studio", SettingsSummary.Names(["Claude", "Copilot Studio"]));
    }

    [Fact]
    public void TokensExpiringSoonFollowsAccessTokenExpiry()
    {
        var tokens = new[] { Token("Pia", Now.AddDays(3)), Token("Pia", Now.AddDays(30)), Token("Pat"), Token("Pat", Now.AddDays(-1)) };

        Assert.Equal(1, SettingsSummary.TokensExpiringSoon(tokens, Now));
    }

    [Fact]
    public void BadgeLabelsNameTheCount()
    {
        Assert.Equal("1 token expires soon", SettingsSummary.TokensExpiringSoonLabel(1));
        Assert.Equal("2 tokens expire soon", SettingsSummary.TokensExpiringSoonLabel(2));
        Assert.Equal("1 failed Event", SettingsSummary.FailedEventsLabel(1));
        Assert.Equal("3 failed Events", SettingsSummary.FailedEventsLabel(3));
    }
}
