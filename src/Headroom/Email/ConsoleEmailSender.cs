namespace Headroom.Email;

// Exists only in Development: production must not log codes.
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> log) : IEmailSender
{
    public Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct)
    {
        log.LogWarning("Login code for {Email} ({Company}): {Code}", toAddress, companyName, code);
        return Task.CompletedTask;
    }
}
