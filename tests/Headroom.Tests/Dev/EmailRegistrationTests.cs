using Headroom.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Headroom.Tests.Dev;

public class EmailRegistrationTests
{
    [Theory]
    [InlineData("Development", "", typeof(ConsoleEmailSender))]
    [InlineData("Development", null, typeof(ConsoleEmailSender))]
    [InlineData("Development", "localhost", typeof(SmtpEmailSender))] // the local preview sends to Mailpit
    [InlineData("Production", "", typeof(SmtpEmailSender))]
    public void PicksTheSenderPerEnvironment(string environment, string? smtpHost, Type expected)
    {
        var services = new ServiceCollection();
        services.AddEmailSender(new FakeEnvironment(environment), smtpHost);

        Assert.Equal(expected, Assert.Single(services, s => s.ServiceType == typeof(IEmailSender)).ImplementationType);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
