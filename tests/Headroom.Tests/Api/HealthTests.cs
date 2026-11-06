using System.Net;
using Headroom.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class HealthTests : IClassFixture<HeadroomApp>
{
    private readonly HeadroomApp _app;

    public HealthTests(HeadroomApp app) => _app = app;

    [Fact]
    public async Task Health_is_200_without_login_and_without_details_when_the_database_answers()
    {
        var response = await _app.Factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_is_503_when_the_database_is_down()
    {
        await using var down = new HeadroomApp();
        await down.InitializeAsync();
        using (var scope = down.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);

        var response = await down.Factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
