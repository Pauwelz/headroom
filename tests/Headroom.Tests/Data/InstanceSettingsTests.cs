using Headroom.Data;

namespace Headroom.Tests.Data;

public class InstanceSettingsTests
{
    [Fact]
    public void NewInstanceSettingsGetsAVersion7Id()
    {
        var settings = new InstanceSettings();

        Assert.Equal(7, settings.Id.Version);
    }
}
