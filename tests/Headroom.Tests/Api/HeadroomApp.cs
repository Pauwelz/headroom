using Headroom.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Starts the app against its own database in the shared PostgreSQL container. Share one per test class.
// All tests in the class share the host, the database and Mail. Filter Mail.Sent by address.
public class HeadroomApp : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeEmailSender Mail { get; } = new();

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = ConnectionString = await TestSql.NewDatabaseAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Headroom"] = connectionString }));
            builder.ConfigureServices(services => services.AddSingleton<IEmailSender>(Mail));
            ConfigureWebHost(builder);
        });
    }

    // Lets a fixture add settings or services for all tests of its class.
    protected virtual void ConfigureWebHost(IWebHostBuilder builder) { }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (ConnectionString is not null) await TestSql.DropDatabaseAsync(ConnectionString);
    }
}
