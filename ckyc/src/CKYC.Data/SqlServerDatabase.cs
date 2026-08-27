using System.Data.Common;
using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CKYC.Data;

/// <summary>
/// SQL Server-backed connection factory + options source. Schema bootstrap happens
/// out-of-band via scripts/sqlserver/schema.sql (db-first), so initialization only
/// verifies connectivity. The seed rows (activity_type, status_master) are part of
/// the schema script; this class never re-seeds.
/// </summary>
public sealed class SqlServerDatabase : ICkycDatabase
{
    private readonly DatabaseSettings _settings;
    private readonly PooledDbContextFactory<CkycDbContext> _contextFactory;
    private bool _initialized;

    public SqlServerDatabase(DatabaseSettings settings)
    {
        if (!string.Equals(settings.Provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Database provider '{settings.Provider}' is not supported. Configure 'sqlserver'.",
                nameof(settings));

        _settings = settings;
        ConnectionString = settings.ConnectionString;

        var builder = new DbContextOptionsBuilder<CkycDbContext>()
            .UseSqlServer(ConnectionString, sql =>
            {
                if (_settings.CommandTimeoutSeconds > 0)
                    sql.CommandTimeout(_settings.CommandTimeoutSeconds);
            });
        _contextFactory = new PooledDbContextFactory<CkycDbContext>(builder.Options, poolSize: 128);
    }

    public string ConnectionString { get; }

    /// <summary>Always false — retained on the interface for reporting/diagnostics.</summary>
    public bool IsSqlite => false;

    public DbConnection Create()
    {
        var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    internal CkycDbContext CreateContext() => _contextFactory.CreateDbContext();

    /// <summary>
    /// The schema is created/owned by scripts/sqlserver/schema.sql (DB-first). Startup only
    /// verifies that the expected root table exists and never runs DDL.
    /// </summary>
    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        // SQL Server is database-first: deployment owns DDL and the application identity
        // only verifies the expected schema. This check is intentionally performed for both
        // flag values; CreateSchemaOnStartup is retained for configuration compatibility.
        await using (var conn = Create())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.master_record', N'U') IS NULL
                    THROW 50000, 'Schema missing: run scripts/sqlserver/schema.sql first (db-first).', 1;

                IF EXISTS (
                    SELECT 1
                      FROM INFORMATION_SCHEMA.COLUMNS
                     WHERE TABLE_SCHEMA = 'dbo'
                       AND TABLE_NAME = 'individual_record_40'
                       AND COLUMN_NAME IN ('PermMatchOvd', 'CurrMatchOvd', 'CurrAddressExactlyMatch')
                       AND CHARACTER_MAXIMUM_LENGTH < 13
                )
                    THROW 50002, 'Schema is outdated: run scripts/sqlserver/migrations/20260827_fix_address_match_width.sql.', 1;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        _initialized = true;
    }
}

/// <summary>Shared helpers for repositories built on the EF Core context.</summary>
public static class CkycDbContextFactory
{
    /// <summary>Creates a short-lived <see cref="CkycDbContext"/> from the database's options.</summary>
    public static CkycDbContext CreateContext(this ICkycDatabase database)
    {
        if (database is not SqlServerDatabase sqlServer)
            throw new InvalidOperationException("Only the SQL Server provider is supported.");
        return sqlServer.CreateContext();
    }

    /// <summary>
    /// Serializes a short database workflow across processes for the duration of its current
    /// transaction. SQL Server application locks avoid select/update claim races without
    /// holding a process-wide CLR lock or requiring a schema-specific lock table.
    /// </summary>
    public static async Task AcquireTransactionLockAsync(this CkycDbContext db, string resource,
        CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource={{resource}},
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=30000;
            IF @result < 0
                THROW 50001, 'Could not acquire the CKYC transaction lock.', 1;
            """, ct);
        _ = affected;
    }
}
