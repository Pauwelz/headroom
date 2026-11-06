namespace Headroom.Email;

// Exists only in Development: production must not log codes.
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> log) : IEmailSender
{
    public Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct)
    {
        // The address stays out of the log: it is personal data.
        log.LogWarning("Login code ({Company}): {Code}", companyName, code);
        return Task.CompletedTask;
    }
}
