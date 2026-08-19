using System.Text.Json;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MetadataExtractor <output-json>");
    return 2;
}

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
var appSettingsPath = Path.Combine(repositoryRoot, "ColdChainX.API", "appsettings.Development.json");

IReadOnlyList<TableMetadata>? tables = null;
var source = "EF Core model snapshot (ApplicationDbContext)";

try
{
    if (File.Exists(appSettingsPath))
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath));
        var connectionString = settings.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("LocalConnection")
            .GetString();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            tables = await ReadLivePostgresAsync(connectionString);
            if (tables.Count > 0)
            {
                source = "Live PostgreSQL metadata (read-only, public schema)";
                Console.WriteLine($"Read {tables.Count} application tables from PostgreSQL in read-only mode.");
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Live database introspection unavailable ({ex.GetType().Name}); using the EF Core model.");
}

if (tables is null || tables.Count == 0)
{
    tables = ReadEfCoreModel();
    Console.WriteLine($"Read {tables.Count} application tables from the EF Core relational model.");
}

var payload = new DictionaryMetadata(
    "ColdChainX",
    source,
    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
    tables);

var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(args[0], json);
return 0;

static async Task<IReadOnlyList<TableMetadata>> ReadLivePostgresAsync(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Timeout = 10,
        CommandTimeout = 30,
        ApplicationName = "ColdChainX Attribute Data Dictionary"
    };

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
    {
        await readOnly.ExecuteNonQueryAsync();
    }

    const string sql = """
        WITH primary_key_columns AS (
            SELECT index_rel.indrelid AS table_oid, key_col.attnum
            FROM pg_index index_rel
            CROSS JOIN LATERAL unnest(index_rel.indkey) AS key_col(attnum)
            WHERE index_rel.indisprimary
        )
        SELECT
            table_schema.nspname AS schema_name,
            table_rel.relname AS table_name,
            column_attr.attnum AS ordinal_position,
            column_attr.attname AS column_name,
            format_type(column_attr.atttypid, column_attr.atttypmod) AS data_type,
            column_attr.attnotnull AS not_null,
            (primary_key_columns.attnum IS NOT NULL) AS is_primary_key,
            COALESCE(foreign_keys.targets, '') AS foreign_key_targets
        FROM pg_class table_rel
        JOIN pg_namespace table_schema ON table_schema.oid = table_rel.relnamespace
        JOIN pg_attribute column_attr
          ON column_attr.attrelid = table_rel.oid
         AND column_attr.attnum > 0
         AND NOT column_attr.attisdropped
        LEFT JOIN primary_key_columns
          ON primary_key_columns.table_oid = table_rel.oid
         AND primary_key_columns.attnum = column_attr.attnum
        LEFT JOIN LATERAL (
            SELECT string_agg(
                target_rel.relname || '.' || target_attr.attname,
                ', ' ORDER BY constraint_rel.conname)
                AS targets
            FROM pg_constraint constraint_rel
            CROSS JOIN LATERAL unnest(constraint_rel.conkey, constraint_rel.confkey)
                AS key_pair(source_attnum, target_attnum)
            JOIN pg_class target_rel ON target_rel.oid = constraint_rel.confrelid
            JOIN pg_attribute target_attr
              ON target_attr.attrelid = constraint_rel.confrelid
             AND target_attr.attnum = key_pair.target_attnum
            WHERE constraint_rel.contype = 'f'
              AND constraint_rel.conrelid = table_rel.oid
              AND key_pair.source_attnum = column_attr.attnum
        ) AS foreign_keys ON TRUE
        WHERE table_schema.nspname = 'public'
          AND table_rel.relkind IN ('r', 'p')
          AND table_rel.relname <> '__EFMigrationsHistory'
        ORDER BY lower(table_rel.relname), column_attr.attnum;
        """;

    var rows = new List<(string Schema, string Table, ColumnMetadata Column)>();
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var targets = reader.GetString(7)
                .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                new ColumnMetadata(
                    reader.GetInt16(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    targets)));
        }
    }

    await transaction.CommitAsync();

    return rows
        .GroupBy(row => new { row.Schema, row.Table })
        .OrderBy(group => group.Key.Table, StringComparer.OrdinalIgnoreCase)
        .Select((group, index) => new TableMetadata(
            index + 1,
            group.Key.Schema,
            group.Key.Table,
            group.OrderBy(row => row.Column.Ordinal).Select(row => row.Column).ToList()))
        .ToList();
}

static IReadOnlyList<TableMetadata> ReadEfCoreModel()
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=metadata_only;Username=metadata_only;Password=metadata_only")
        .Options;

    using var context = new ApplicationDbContext(options);
    var tableColumns = new Dictionary<(string Schema, string Table), Dictionary<string, MutableColumn>>();
    var sequence = 0;

    foreach (var entityType in context.Model.GetEntityTypes())
    {
        var tableName = entityType.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            continue;
        }

        var schemaName = entityType.GetSchema() ?? "public";
        var storeObject = StoreObjectIdentifier.Table(tableName, schemaName);
        var key = (Schema: schemaName, Table: tableName);
        if (!tableColumns.TryGetValue(key, out var columns))
        {
            columns = new Dictionary<string, MutableColumn>(StringComparer.OrdinalIgnoreCase);
            tableColumns[key] = columns;
        }

        var primaryKeyProperties = entityType.FindPrimaryKey()?.Properties.ToHashSet() ?? [];
        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.GetColumnName(storeObject);
            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            if (!columns.TryGetValue(columnName, out var column))
            {
                column = new MutableColumn
                {
                    Sequence = sequence++,
                    Name = columnName,
                    DataType = property.GetColumnType(storeObject) ?? property.GetColumnType() ?? property.ClrType.Name,
                    NotNull = !property.IsColumnNullable(storeObject)
                };
                columns[columnName] = column;
            }

            column.IsPrimaryKey |= primaryKeyProperties.Contains(property);
        }

        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            var principalType = foreignKey.PrincipalEntityType;
            var principalTable = principalType.GetTableName();
            if (string.IsNullOrWhiteSpace(principalTable))
            {
                continue;
            }

            var principalSchema = principalType.GetSchema() ?? "public";
            var principalStoreObject = StoreObjectIdentifier.Table(principalTable, principalSchema);
            for (var i = 0; i < foreignKey.Properties.Count; i++)
            {
                var sourceColumnName = foreignKey.Properties[i].GetColumnName(storeObject);
                var targetColumnName = foreignKey.PrincipalKey.Properties[i].GetColumnName(principalStoreObject);
                if (sourceColumnName is null || targetColumnName is null || !columns.TryGetValue(sourceColumnName, out var column))
                {
                    continue;
                }

                column.ForeignKeys.Add($"{principalTable}.{targetColumnName}");
            }
        }
    }

    return tableColumns
        .OrderBy(pair => pair.Key.Table, StringComparer.OrdinalIgnoreCase)
        .Select((pair, index) => new TableMetadata(
            index + 1,
            pair.Key.Schema,
            pair.Key.Table,
            pair.Value.Values
                .OrderByDescending(column => column.IsPrimaryKey)
                .ThenBy(column => column.Sequence)
                .Select((column, ordinal) => new ColumnMetadata(
                    ordinal + 1,
                    column.Name,
                    column.DataType,
                    column.NotNull,
                    column.IsPrimaryKey,
                    column.ForeignKeys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList()))
        .ToList();
}

record ColumnMetadata(
    int Ordinal,
    string AttributeName,
    string DataType,
    bool NotNull,
    bool IsPrimaryKey,
    IReadOnlyList<string> ForeignKeys);

record TableMetadata(
    int No,
    string SchemaName,
    string TableName,
    IReadOnlyList<ColumnMetadata> Columns);

record DictionaryMetadata(
    string Project,
    string Source,
    string GeneratedAt,
    IReadOnlyList<TableMetadata> Tables);

sealed class MutableColumn
{
    public int Sequence { get; init; }
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool NotNull { get; init; }
    public bool IsPrimaryKey { get; set; }
    public HashSet<string> ForeignKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
}
