#:package Microsoft.Data.SqlClient@7.1.1
#:package Npgsql@10.0.3
#:package System.Security.Cryptography.Pkcs@9.0.22
#:property RestorePackagesWithLockFile=false

// The Pkcs pin replaces the version SqlClient pulls in, which has known CVEs (the repo treats warnings as errors).
// One-time copy of a Headroom 0.1.0 SQL Server database into the PostgreSQL schema that the new app created.
// It empties the PostgreSQL tables, copies every table in foreign-key order in one transaction, and rolls back on any error or count mismatch.
// Steps: docs/guides/operator-guide.md, section "Move from SQL Server".
// Usage: dotnet run scripts/migrate-sqlserver-to-postgres.cs -- "<SQL Server connection string>" "<PostgreSQL connection string>"

using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

if (args.Length != 2 || args.Any(string.IsNullOrWhiteSpace))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/migrate-sqlserver-to-postgres.cs -- \"<SQL Server connection string>\" \"<PostgreSQL connection string>\"");
    return 2;
}

try
{
    // The builders reject malformed strings. Their messages do not repeat the password.
    var sqlConnectionString = new SqlConnectionStringBuilder(args[0]).ConnectionString;
    var pgConnectionString = new NpgsqlConnectionStringBuilder(args[1]).ConnectionString;

    await using var src = new SqlConnection(sqlConnectionString);
    await using var dst = new NpgsqlConnection(pgConnectionString);
    await src.OpenAsync();
    await dst.OpenAsync();

    var columns = new Dictionary<string, List<(string Name, string Type)>>();
    await using (var cmd = new NpgsqlCommand(
        """
        SELECT c.table_name, c.column_name, c.data_type
        FROM information_schema.columns c
        JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND c.table_name <> '__EFMigrationsHistory'
        ORDER BY c.table_name, c.ordinal_position
        """, dst))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            if (!columns.TryGetValue(r.GetString(0), out var list)) columns[r.GetString(0)] = list = [];
            list.Add((r.GetString(1), r.GetString(2)));
        }
    if (columns.Count == 0) throw new InvalidOperationException("The PostgreSQL database has no tables. Start the new app once first.");

    // Parents first. Self-references are ignored.
    var parents = columns.Keys.ToDictionary(t => t, _ => new HashSet<string>());
    await using (var cmd = new NpgsqlCommand(
        "SELECT conrelid::regclass::text, confrelid::regclass::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace", dst))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            string child = r.GetString(0).Trim('"'), parent = r.GetString(1).Trim('"');
            if (child != parent && parents.ContainsKey(child)) parents[child].Add(parent);
        }
    var order = new List<string>();
    while (order.Count < columns.Count)
    {
        var ready = parents.Where(p => !order.Contains(p.Key) && p.Value.All(order.Contains)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
        if (ready.Count == 0) throw new InvalidOperationException("The foreign keys form a cycle.");
        order.AddRange(ready);
    }

    // Check the source before changing anything.
    foreach (var table in order)
    {
        var have = new HashSet<string>();
        await using var cmd = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t", src);
        cmd.Parameters.AddWithValue("@t", table);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) have.Add(r.GetString(0));
        if (have.Count == 0) throw new InvalidOperationException($"Source table {table} does not exist.");
        foreach (var (name, _) in columns[table])
            if (!have.Contains(name)) throw new InvalidOperationException($"Source column {table}.{name} does not exist.");
    }

    var pgq = (string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    var sqlq = (string name) => "[" + name.Replace("]", "]]") + "]";
    var copied = new Dictionary<string, long>();

    await using var tx = await dst.BeginTransactionAsync();
    foreach (var table in Enumerable.Reverse(order))
        await new NpgsqlCommand($"DELETE FROM {pgq(table)}", dst, tx).ExecuteNonQueryAsync();

    foreach (var table in order)
    {
        var cols = columns[table];
        await using var read = await new SqlCommand($"SELECT {string.Join(", ", cols.Select(c => sqlq(c.Name)))} FROM {sqlq(table)}", src).ExecuteReaderAsync();
        await using var writer = await dst.BeginBinaryImportAsync($"COPY {pgq(table)} ({string.Join(", ", cols.Select(c => pgq(c.Name)))}) FROM STDIN (FORMAT BINARY)");
        long n = 0;
        while (await read.ReadAsync())
        {
            await writer.StartRowAsync();
            for (var i = 0; i < cols.Count; i++)
            {
                var type = ToNpgsqlType(cols[i].Type);
                if (await read.IsDBNullAsync(i)) { await writer.WriteNullAsync(); continue; }
                object v = read.GetValue(i);
                v = (v, type) switch
                {
                    (DateTimeOffset d, NpgsqlDbType.TimestampTz) => d.ToUniversalTime(),
                    (DateTime d, NpgsqlDbType.TimestampTz) => DateTime.SpecifyKind(d, DateTimeKind.Utc), // shortcut: assumes datetime2 holds UTC, as the old app wrote it
                    (DateTime d, NpgsqlDbType.Date) => DateOnly.FromDateTime(d),
                    _ => v,
                };
                await writer.WriteAsync(v, type);
            }
            n++;
        }
        await writer.CompleteAsync();
        copied[table] = n;
    }

    // Identity or serial columns: continue after the highest copied value.
    foreach (var table in order)
        foreach (var (name, _) in columns[table])
        {
            var seq = await new NpgsqlCommand("SELECT pg_get_serial_sequence($1, $2)", dst, tx)
                { Parameters = { new() { Value = pgq(table) }, new() { Value = name } } }.ExecuteScalarAsync() as string;
            if (seq is null) continue;
            await new NpgsqlCommand(
                $"SELECT setval($1, COALESCE(MAX({pgq(name)}), 1), COUNT(*) > 0) FROM {pgq(table)}", dst, tx)
                { Parameters = { new() { Value = seq } } }.ExecuteNonQueryAsync();
        }

    var failed = false;
    Console.WriteLine($"{"Table",-28} {"Copied",8} {"Source",8}");
    foreach (var table in order)
    {
        var source = Convert.ToInt64(await new SqlCommand($"SELECT COUNT(*) FROM {sqlq(table)}", src).ExecuteScalarAsync());
        var target = Convert.ToInt64(await new NpgsqlCommand($"SELECT COUNT(*) FROM {pgq(table)}", dst, tx).ExecuteScalarAsync());
        var ok = copied[table] == source && target == source;
        failed |= !ok;
        Console.WriteLine($"{table,-28} {copied[table],8} {source,8}{(ok ? "" : "  MISMATCH")}");
    }
    if (failed) { await tx.RollbackAsync(); Console.Error.WriteLine("Counts differ. Rolled back."); return 1; }
    await tx.CommitAsync();
    Console.WriteLine("Done. Counts match.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed, nothing was committed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static NpgsqlDbType ToNpgsqlType(string dataType) => dataType switch
{
    "date" => NpgsqlDbType.Date,
    "uuid" => NpgsqlDbType.Uuid,
    "bytea" => NpgsqlDbType.Bytea,
    "numeric" => NpgsqlDbType.Numeric,
    "boolean" => NpgsqlDbType.Boolean,
    "text" => NpgsqlDbType.Text,
    "character varying" => NpgsqlDbType.Varchar,
    "timestamp with time zone" => NpgsqlDbType.TimestampTz,
    "timestamp without time zone" => NpgsqlDbType.Timestamp,
    "integer" => NpgsqlDbType.Integer,
    "bigint" => NpgsqlDbType.Bigint,
    "smallint" => NpgsqlDbType.Smallint,
    _ => throw new NotSupportedException($"Column type {dataType} is not supported."),
};
