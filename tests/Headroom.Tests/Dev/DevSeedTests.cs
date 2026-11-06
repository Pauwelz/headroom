using Headroom.Data;
using Headroom.Dev;
using Headroom.Tests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headroom.Tests.Dev;

public class DevSeedTests(HeadroomApp app) : IClassFixture<HeadroomApp>
{
    [Fact]
    public async Task SeedsAnEmptyDatabaseOnceAndOnlyInDevelopment()
    {
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var seed = new DevSeed(db, TimeProvider.System, NullLogger<DevSeed>.Instance);

        Assert.False(await seed.RunAsync(Environments.Production, null, TestContext.Current.CancellationToken));
        Assert.False(await db.InstanceSettings.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(await seed.RunAsync(Environments.Development, null, TestContext.Current.CancellationToken));
        Assert.Equal("Demo BV", (await db.InstanceSettings.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).CompanyName);
        Assert.Equal(1, await db.People.CountAsync(p => p.IsPlanner && p.Email == "planner@example.test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.People.CountAsync(p => !p.IsPlanner && p.Email == "person@example.test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(14, await db.Contracts.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(3, await db.Clients.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(5, await db.Projects.CountAsync(p => p.Status == ProjectStatus.Confirmed, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.Projects.CountAsync(p => p.Status == ProjectStatus.Tentative, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(await db.Allocations.CountAsync(cancellationToken: TestContext.Current.CancellationToken) >= 3);
        Assert.Equal(4, await db.TimeOff.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.CompanyDaysOff.CountAsync(cancellationToken: TestContext.Current.CancellationToken));

        var allocations = await db.Allocations.CountAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(await seed.RunAsync(Environments.Development, null, TestContext.Current.CancellationToken));
        Assert.Equal(allocations, await db.Allocations.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(14, await db.People.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}

// A separate class, so the test gets its own empty database.
public class SeedSettingTests(HeadroomApp app) : IClassFixture<HeadroomApp>
{
    [Fact]
    public async Task SeedsOutsideDevelopmentWhenThePlannerEmailIsSet()
    {
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var seed = new DevSeed(db, TimeProvider.System, NullLogger<DevSeed>.Instance);

        Assert.False(await seed.RunAsync(Environments.Production, "", TestContext.Current.CancellationToken));
        Assert.True(await seed.RunAsync(Environments.Production, "tester@example.com", TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.People.CountAsync(p => p.IsPlanner && p.Email == "tester@example.com", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.People.CountAsync(p => p.Email == "person@example.test", cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await seed.RunAsync(Environments.Production, "tester@example.com", TestContext.Current.CancellationToken));
    }
}
