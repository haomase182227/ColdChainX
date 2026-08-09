using Npgsql;

const string connectionString =
    "Host=coldchainx-db-server.postgres.database.azure.com;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true";

var execute = args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase));
var start = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Unspecified);
var end = new DateTime(2026, 8, 8, 20, 5, 0, DateTimeKind.Unspecified);

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

Console.WriteLine($"Database: {conn.Database}");
Console.WriteLine($"Mode: {(execute ? "DELETE" : "PREVIEW")}");
Console.WriteLine($"UTC timestamp range: [{start:yyyy-MM-dd HH:mm:ss}, {end:yyyy-MM-dd HH:mm:ss})");
Console.WriteLine();

var tables = await GetCreatedAtTables(conn);
var depths = await GetFkDepths(conn, tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));

foreach (var table in tables)
{
    table.Depth = depths.TryGetValue(table.Name, out var depth) ? depth : 0;
    table.Count = await CountRows(conn, table.Name, start, end);
}

var affectedTables = tables
    .Where(t => t.Count > 0)
    .OrderByDescending(t => t.Depth)
    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine("Rows found by created_at:");
foreach (var table in affectedTables)
    Console.WriteLine($"{table.Name,-40} depth={table.Depth,2} rows={table.Count}");
Console.WriteLine($"Total rows matched: {affectedTables.Sum(t => t.Count)}");

if (!execute)
{
    Console.WriteLine();
    Console.WriteLine("Preview only. Re-run with --execute to delete.");
    return;
}

Console.WriteLine();
Console.WriteLine("Deleting...");

var errors = new List<string>();
await using var tx = await conn.BeginTransactionAsync();
foreach (var table in affectedTables)
{
    try
    {
        var deleted = await DeleteRows(conn, table.Name, start, end);
        Console.WriteLine($"{table.Name,-40} deleted={deleted}");
    }
    catch (Exception ex)
    {
        errors.Add($"{table.Name}: {ex.Message}");
        Console.WriteLine($"{table.Name,-40} FAILED: {ex.Message}");
    }
}

if (errors.Count > 0)
{
    await tx.RollbackAsync();
    Console.WriteLine();
    Console.WriteLine("Rolled back because at least one table failed.");
    foreach (var error in errors) Console.WriteLine(error);
    Environment.ExitCode = 1;
    return;
}

await tx.CommitAsync();
Console.WriteLine("Cleanup committed.");

static async Task<List<TableInfo>> GetCreatedAtTables(NpgsqlConnection conn)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        select quote_ident(table_schema) || '.' || quote_ident(table_name)
        from information_schema.columns
        where table_schema = 'public'
          and column_name = 'created_at'
        order by table_name;
        """;

    var result = new List<TableInfo>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        result.Add(new TableInfo(reader.GetString(0)));
    return result;
}

static async Task<Dictionary<string, int>> GetFkDepths(NpgsqlConnection conn, HashSet<string> knownTables)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        select
          quote_ident(tc.table_schema) || '.' || quote_ident(tc.table_name) as child_table,
          quote_ident(ccu.table_schema) || '.' || quote_ident(ccu.table_name) as parent_table
        from information_schema.table_constraints tc
        join information_schema.key_column_usage kcu
          on tc.constraint_name = kcu.constraint_name
         and tc.table_schema = kcu.table_schema
        join information_schema.constraint_column_usage ccu
          on ccu.constraint_name = tc.constraint_name
         and ccu.table_schema = tc.table_schema
        where tc.constraint_type = 'FOREIGN KEY'
          and tc.table_schema = 'public';
        """;

    var parentsByChild = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var child = reader.GetString(0);
            var parent = reader.GetString(1);
            if (!knownTables.Contains(child) || !knownTables.Contains(parent)) continue;
            if (!parentsByChild.TryGetValue(child, out var parents))
            {
                parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                parentsByChild[child] = parents;
            }
            parents.Add(parent);
        }
    }

    var memo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int Depth(string table, HashSet<string> visiting)
    {
        if (memo.TryGetValue(table, out var cached)) return cached;
        if (!parentsByChild.TryGetValue(table, out var parents) || parents.Count == 0) return memo[table] = 0;
        if (!visiting.Add(table)) return 0;
        var depth = 1 + parents.Select(parent => Depth(parent, visiting)).DefaultIfEmpty(0).Max();
        visiting.Remove(table);
        return memo[table] = depth;
    }

    foreach (var table in knownTables) Depth(table, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    return memo;
}

static async Task<long> CountRows(NpgsqlConnection conn, string table, DateTime start, DateTime end)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = $"select count(*) from {table} where created_at >= @start and created_at < @end";
    cmd.Parameters.AddWithValue("start", start);
    cmd.Parameters.AddWithValue("end", end);
    return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
}

static async Task<long> DeleteRows(NpgsqlConnection conn, string table, DateTime start, DateTime end)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = $"delete from {table} where created_at >= @start and created_at < @end";
    cmd.Parameters.AddWithValue("start", start);
    cmd.Parameters.AddWithValue("end", end);
    return await cmd.ExecuteNonQueryAsync();
}

sealed class TableInfo(string name)
{
    public string Name { get; } = name;
    public int Depth { get; set; }
    public long Count { get; set; }
}
