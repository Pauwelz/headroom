using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Headroom.Tests.Api;

// Starts the app against its own database in the shared PostgreSQL container. Share one per test class.
public sealed class HeadroomApp : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = ConnectionString = await TestSql.NewDatabaseAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Headroom"] = connectionString })));
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        if (ConnectionString is not null) await TestSql.DropDatabaseAsync(ConnectionString);
    }
}
