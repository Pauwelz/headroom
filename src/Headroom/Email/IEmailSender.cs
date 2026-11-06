namespace Headroom.Email;

// The seam for outgoing email. Tests replace it with a fake.
public interface IEmailSender
{
    // The From name is "<company name> Headroom".
    Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct);
}
