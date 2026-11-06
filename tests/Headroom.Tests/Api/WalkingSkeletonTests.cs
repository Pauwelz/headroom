using System.Net;
using Headroom.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class WalkingSkeletonTests(HeadroomApp app) : IClassFixture<HeadroomApp>
{
    [Fact]
    public async Task ServesTheOpenApiDocument()
    {
        var response = await app.Factory.CreateClient().GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DoesNotServeTheOpenApiDocumentOutsideDevelopment()
    {
        var production = app.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ServesThePlaceholderPage()
    {
        var response = await app.Factory.CreateClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartupMigratesTheDatabaseAndTheInstanceNeedsALogin()
    {
        using (var scope = app.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The Instance id is not public. The login page gets the company name another way.
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Factory.CreateClient().GetAsync("/api/instance", TestContext.Current.CancellationToken)).StatusCode);
    }
}
