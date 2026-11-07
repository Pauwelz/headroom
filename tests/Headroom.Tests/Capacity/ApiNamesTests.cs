using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;

namespace Headroom.Tests.Capacity;

public class ApiNamesTests
{
    [Fact]
    public void EachProjectStatusHasOneApiName() =>
        Assert.Equal(ProjectStatusName.All, Enum.GetValues<ProjectStatus>().Select(s => new Project { Status = s }.StatusName));

    [Theory]
    [InlineData(DayPart.Morning, "morning")]
    [InlineData(DayPart.Afternoon, "afternoon")]
    public void ADayPartMapsToItsNameAndBack(DayPart part, string name)
    {
        Assert.Equal(name, DayPartNames.ToApi(part));
        Assert.True(DayPartNames.TryParse(name.ToUpperInvariant(), out var parsed));
        Assert.Equal(part, parsed);
    }

    [Theory]
    [InlineData("noon")]
    [InlineData("0")]
    [InlineData(null)]
    public void AnUnknownDayPartNameIsRefused(string? name) => Assert.False(DayPartNames.TryParse(name, out _));

    [Fact]
    public void TheClientAndTheServerAgreeOnTheDayOffTypeNames()
    {
        // Both sides use DayOffTypeName, so pin its values: they are the wire contract.
        Assert.Equal("PublicHoliday", DayOffTypeName.PublicHoliday);
        Assert.Equal("CompanyDayOff", DayOffTypeName.CompanyDayOff);
    }

    [Fact]
    public void AProjectLabelIsClientSlashProject() =>
        Assert.Equal("Acme / Website", ProjectLabel.Format("Acme", "Website"));
}
