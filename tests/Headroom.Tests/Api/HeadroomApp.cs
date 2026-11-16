using Headroom.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Starts the app against its own database in the shared PostgreSQL container. Share one per test class.
// The login goes to a fake Entra ID (Provider). TestLogin.SignInAsync uses it.
public class HeadroomApp : IAsyncLifetime
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string ClientSecret = "client-secret-value";

    public static readonly Dictionary<string, string?> EntraIdSettings = new()
    {
        ["EntraId:TenantId"] = TenantId, ["EntraId:ClientId"] = "headroom", ["EntraId:ClientSecret"] = ClientSecret,
    };

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeProvider Provider { get; } = new();

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = ConnectionString = await TestSql.NewDatabaseAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>(EntraIdSettings) { ["ConnectionStrings:Headroom"] = connectionString }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Provider);
                services.PostConfigure<OpenIdConnectOptions>(CompanyLogin.Scheme, Provider.Configure);
            });
            ConfigureWebHost(builder);
        });
    }

    // Lets a fixture add settings or services for all tests of its class.
    protected virtual void ConfigureWebHost(IWebHostBuilder builder) { }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        Provider.Dispose();
        if (ConnectionString is not null) await TestSql.DropDatabaseAsync(ConnectionString);
    }
}
