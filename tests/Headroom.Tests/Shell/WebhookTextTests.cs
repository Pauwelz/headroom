using Headroom.Client.Settings;

namespace Headroom.Tests.Shell;

public class WebhookTextTests
{
    [Fact]
    public void DeleteConfirmationSaysTheUnsentEventsAreDeletedToo()
    {
        var text = WebhookText.DeleteConfirmation("Teams flow");

        Assert.Contains("Teams flow", text);
        Assert.Contains("unsent Events", text);
    }
}
