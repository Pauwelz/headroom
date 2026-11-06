using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Headroom.Tests;

// One SQL Server container for all test runs on this machine, also across worktrees and agents.
// Each container start adds a network interface, and Chrome on Linux drops its connections on every interface change.
// Reuse disables Ryuk, so the container keeps running after the tests. docs/testing.md names the command that stops it.
internal static class TestSql
{
    private static readonly Lazy<Task<MsSqlContainer>> Container = new(async () =>
    {
        var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
            .WithLabel("headroom.test-sql", "true") // The stop command in docs/testing.md filters on this label.
            .WithReuse(true)
            .Build();
        await sql.StartAsync();
        return sql;
    });

    // A connection string to a database that does not exist yet. EF migrations create it.
    public static async Task<string> NewDatabaseAsync()
    {
        var sql = await Container.Value;
        return new SqlConnectionStringBuilder(sql.GetConnectionString()) { InitialCatalog = $"t{Guid.NewGuid():N}" }.ConnectionString;
    }
}
