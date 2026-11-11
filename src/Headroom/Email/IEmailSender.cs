namespace Headroom.Email;

// The seam for outgoing email. Tests replace it with a fake.
public interface IEmailSender
{
    // The From name is "<company name> Headroom".
    // With a server, the email goes through exactly that server (the first-run screen, before anything is stored).
    Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct, SmtpOptions? server = null);

    // Sends a short test email through exactly this server, whatever the stored settings say.
    Task SendTestAsync(SmtpOptions server, string toAddress, string companyName, CancellationToken ct);
}
