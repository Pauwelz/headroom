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
            .WithDockerEndpoint("unix:///var/run/docker.sock") // the local daemon, even when another Docker context is active
            .WithCommand("-c", "max_connections=500") // Each test database keeps its own idle pool.
            .WithLabel("headroom.test-sql", "true") // The stop command in docs/testing.md filters on this label.
            .WithReuse(true)
            .Build();
        await sql.StartAsync();
        await DropStaleDatabasesAsync(sql.GetConnectionString());
        return sql;
    });

    // A test database lives at most this long. Older ones come from a run that was killed before it could drop them.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    // A connection string to a database that does not exist yet. EF migrations create it.
    // The name holds its creation time, so a later run can find leftovers without dropping databases that other runs still use.
    public static async Task<string> NewDatabaseAsync()
    {
        var sql = await Container.Value;
        var name = $"t{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid():N}";
        return new NpgsqlConnectionStringBuilder(sql.GetConnectionString()) { Database = name }.ConnectionString;
    }

    // Closes the pooled connections of the database, then drops it. Does nothing if EF never created it.
    public static async Task DropDatabaseAsync(string connectionString)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        await DropAsync(connectionString, new NpgsqlConnectionStringBuilder(connectionString).Database!);
    }

    internal static async Task DropStaleDatabasesAsync(string connectionString)
    {
        var cutoff = DateTimeOffset.UtcNow - StaleAfter;
        await using var connection = await OpenAdminAsync(connectionString);
        await using var command = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname ~ '^t[0-9]+_'", connection);
        var stale = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (DateTimeOffset.FromUnixTimeSeconds(long.Parse(name[1..name.IndexOf('_')])) < cutoff) stale.Add(name);
            }
        foreach (var name in stale) await DropAsync(connectionString, name);
    }

    private static async Task DropAsync(string connectionString, string database)
    {
        await using var connection = await OpenAdminAsync(connectionString);
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> OpenAdminAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
