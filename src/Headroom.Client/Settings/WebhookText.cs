namespace Headroom.Client.Settings;

public static class WebhookText
{
    public static string DeleteConfirmation(string name) => $"Delete the Webhook \"{name}\"? Its unsent Events are deleted too.";
}
