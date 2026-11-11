using Headroom.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Headroom.Email;

public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
}

// Picks the email server at each send (see EmailServer.Resolve).
public class EmailSender(
    HeadroomDbContext db, IOptions<SmtpOptions> environment, IHostEnvironment host,
    IDataProtectionProvider protection, ILogger<EmailSender> log) : IEmailSender
{
    public async Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct)
    {
        var stored = await EmailServer.ReadStoredAsync(db, protection, ct);
        var source = EmailServer.Resolve(stored is not null, environment.Value.Host, host.IsDevelopment());
        if (source == EmailServer.Source.Console)
        {
            // The address stays out of the log: it is personal data.
            log.LogWarning("Login code ({Company}): {Code}", companyName, code);
            return;
        }
        await SendAsync(stored ?? environment.Value, toAddress, companyName, LoginCodeEmail.Subject, LoginCodeEmail.Body(companyName, code), ct);
    }

    public Task SendTestAsync(SmtpOptions server, string toAddress, string companyName, CancellationToken ct) =>
        SendAsync(server, toAddress, companyName, "Headroom test email",
            new TextPart("plain") { Text = "This is a test email from Headroom. The email server works." }, ct);

    private static async Task SendAsync(SmtpOptions smtp, string toAddress, string companyName, string subject, MimeEntity body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress($"{companyName} Headroom", smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = body;

        using var client = new SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, ct);
        if (smtp.Username.Length > 0) await client.AuthenticateAsync(smtp.Username, smtp.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
