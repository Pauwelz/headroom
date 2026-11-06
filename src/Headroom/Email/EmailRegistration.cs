namespace Headroom.Email;

public static class EmailRegistration
{
    // Development logs codes, unless an SMTP host is set (the local preview sends to Mailpit).
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IHostEnvironment env, string? smtpHost) =>
        env.IsDevelopment() && string.IsNullOrEmpty(smtpHost)
            ? services.AddScoped<IEmailSender, ConsoleEmailSender>()
            : services.AddScoped<IEmailSender, SmtpEmailSender>();
}
