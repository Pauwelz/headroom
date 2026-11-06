using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Headroom.Tests.Data;

// Upgrades a database from the oldest supported version to the newest. See docs/migrations.md.
public sealed class MigrationUpgradeTests
{
    // The last migration of the oldest supported release.
    // Only InitialCreate exists, so this test upgrades InitialCreate to itself. It gains meaning with the second migration.
    // Update it when a release drops support.
    private const string OldestSupportedMigration = "20261106172820_InitialCreate";

    [Fact]
    public async Task OldestSupportedVersionUpgradesToTheNewest()
    {
        var options = new DbContextOptionsBuilder<HeadroomDbContext>().UseSqlServer(await TestSql.NewDatabaseAsync()).Options;
        await using var db = new HeadroomDbContext(options, TimeProvider.System);
        // Fails if the constant names no migration.
        Assert.Contains(OldestSupportedMigration, db.Database.GetMigrations());
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(OldestSupportedMigration, TestContext.Current.CancellationToken);
        Assert.Equal(OldestSupportedMigration, (await db.Database.GetAppliedMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken)).Last());

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
