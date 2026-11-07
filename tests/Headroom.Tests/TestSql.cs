using Npgsql;
using Testcontainers.PostgreSql;

namespace Headroom.Tests;

// One PostgreSQL container for all test runs on this machine, also across worktrees and agents.
// Each container start adds a network interface, and Chrome on Linux drops its connections on every interface change.
// Reuse disables Ryuk, so the container keeps running after the tests. docs/testing.md names the command that stops it.
internal static class TestSql
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var sql = new PostgreSqlBuilder("postgres:17")
            .WithCommand("-c", "max_connections=500") // Each test database keeps its own idle pool.
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
        return new NpgsqlConnectionStringBuilder(sql.GetConnectionString()) { Database = $"t{Guid.NewGuid():N}" }.ConnectionString;
    }

    // Closes the pooled connections of the database, then drops it. Does nothing if EF never created it.
    public static async Task DropDatabaseAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        target.Database = "postgres";
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(connectionString).Database}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
