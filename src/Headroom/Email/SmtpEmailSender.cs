using MailKit.Net.Smtp;
using MailKit.Security;
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

public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct)
    {
        var smtp = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress($"{companyName} Headroom", smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = LoginCodeEmail.Subject;
        message.Body = LoginCodeEmail.Body(companyName, code);

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, ct);
        if (smtp.Username.Length > 0) await client.AuthenticateAsync(smtp.Username, smtp.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
