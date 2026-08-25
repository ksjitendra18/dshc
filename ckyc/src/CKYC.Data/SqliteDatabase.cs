using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Data.Schema;
using Microsoft.Data.Sqlite;

namespace CKYC.Data;

/// <summary>SQLite-backed connection factory + schema bootstrap.</summary>
public sealed class SqliteDatabase : ICkycDatabase, IDisposable
{
    private readonly DatabaseSettings _settings;
    private bool _initialized;

    static SqliteDatabase() => SQLitePCL.Batteries_V2.Init();

    public SqliteDatabase(DatabaseSettings settings)
    {
        _settings = settings;
        ConnectionString = settings.ConnectionString;
    }

    public string ConnectionString { get; }
    public bool IsSqlite => true;

    public DbConnection Create()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await using var conn = (SqliteConnection)Create();

        // Rename the organization-owned key before creating indexes or running additive
        // migrations. SQLite preserves the data and updates dependent index definitions.
        foreach (var table in Ddl.CustomerIdTables)
        {
            if (!await ColumnExistsAsync(conn, table, "SourceCustomerId", ct) ||
                await ColumnExistsAsync(conn, table, "CustomerId", ct)) continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} RENAME COLUMN SourceCustomerId TO CustomerId";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var sql in Ddl.CreateStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Add any columns that are missing on a database created before a schema change.
        foreach (var (table, column, alterSql) in Ddl.AdditiveMigrations)
        {
            if (await ColumnExistsAsync(conn, table, column, ct)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = alterSql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Handle a partially upgraded database that happens to contain both names.
        foreach (var table in Ddl.CustomerIdTables)
        {
            if (!await ColumnExistsAsync(conn, table, "SourceCustomerId", ct) ||
                !await ColumnExistsAsync(conn, table, "CustomerId", ct)) continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE {table} SET CustomerId=SourceCustomerId WHERE CustomerId IS NULL";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Older child record tables did not carry the identifier at all. Populate those
        // rows through their stable MasterRecordId relationship.
        foreach (var table in Ddl.MasterLinkedCustomerIdTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                UPDATE {table}
                   SET CustomerId=(SELECT m.CustomerId FROM master_record m WHERE m.Id={table}.MasterRecordId)
                 WHERE CustomerId IS NULL
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Create indexes that depend on freshly-added columns (after the migrations ran).
        foreach (var sql in Ddl.PostMigrationStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Seed the activity-type master (idempotent — only rows whose Code is missing).
        foreach (var sql in Ddl.SeedStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Seed the status master lookup (idempotent — only rows whose StatusValue is missing).
        foreach (var sql in Ddl.StatusMasterSeedStatements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        _initialized = true;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var name = r.GetString(1); // column name is the second field of PRAGMA table_info
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
