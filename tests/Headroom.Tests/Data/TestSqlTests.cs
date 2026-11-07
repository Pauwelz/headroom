using Npgsql;

namespace Headroom.Tests.Data;

public sealed class TestSqlTests
{
    [Fact]
    public async Task StaleTestDatabasesAreDroppedAndFreshOnesStay()
    {
        var connectionString = await TestSql.NewDatabaseAsync();
        var stale = $"t{DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds()}_{Guid.NewGuid():N}";
        var fresh = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await RunAsync(connectionString, $"CREATE DATABASE \"{stale}\"");
        await RunAsync(connectionString, $"CREATE DATABASE \"{fresh}\"");

        await TestSql.DropStaleDatabasesAsync(connectionString);

        Assert.False(await ExistsAsync(connectionString, stale));
        Assert.True(await ExistsAsync(connectionString, fresh));
        await TestSql.DropDatabaseAsync(connectionString);
    }

    private static async Task<object?> RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> ExistsAsync(string connectionString, string database) =>
        await RunAsync(connectionString, $"SELECT 1 FROM pg_database WHERE datname = '{database}'") is not null;
}
