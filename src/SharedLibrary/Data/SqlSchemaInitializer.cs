using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TaxpayerAnalytics.Shared.Data;

/// <summary>
/// Idempotent SQL Server bootstrap that:
///   1. Ensures the target database exists (creates it via master if missing).
///   2. Executes a GO-delimited DDL script against the target database.
///
/// The DDL itself is written with IF OBJECT_ID(...) / IF NOT EXISTS guards so
/// re-running on an existing schema is a no-op. We use this at app startup so
/// the user never has to invoke sqlcmd manually.
/// </summary>
public static partial class SqlSchemaInitializer
{
    public static async Task EnsureDatabaseAndSchemaAsync(
        string connectionString,
        string schemaSqlPath,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("connection string required", nameof(connectionString));

        if (!File.Exists(schemaSqlPath))
        {
            logger.LogWarning("Schema file not found at {Path}; skipping auto-apply", schemaSqlPath);
            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        var targetDb = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDb))
            throw new InvalidOperationException("Connection string must specify InitialCatalog (Database=...)");

        await EnsureDatabaseExistsAsync(builder, targetDb, logger, ct);

        var script = await File.ReadAllTextAsync(schemaSqlPath, ct);
        var batches = SplitGoBatches(script);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (string.IsNullOrWhiteSpace(batch)) continue;
            try
            {
                await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                // Surface the failing batch's first line so the operator can fix it quickly.
                var firstLine = batch.Split('\n').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
                logger.LogError(ex, "Schema apply failed on batch #{Idx}: {Snippet}", i + 1, firstLine);
                throw;
            }
        }

        logger.LogInformation("SQL schema applied/verified on database {Db} ({Batches} batches)", targetDb, batches.Count);
    }

    private static async Task EnsureDatabaseExistsAsync(
        SqlConnectionStringBuilder builder,
        string targetDb,
        ILogger logger,
        CancellationToken ct)
    {
        var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { InitialCatalog = "master" };

        await using var master = new SqlConnection(masterBuilder.ConnectionString);
        try { await master.OpenAsync(ct); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Could not connect to SQL Server (master) at {Source}", builder.DataSource);
            throw;
        }

        // Parameterised DB existence check.
        await using (var probe = new SqlCommand("SELECT DB_ID(@db);", master))
        {
            probe.Parameters.AddWithValue("@db", targetDb);
            var dbId = await probe.ExecuteScalarAsync(ct);
            if (dbId is not null && dbId != DBNull.Value) return;
        }

        // Whitelist the DB name before splicing into DDL (CREATE DATABASE cannot be parameterised).
        if (!IdentifierRegex().IsMatch(targetDb))
            throw new InvalidOperationException($"Refusing to create database with unsafe name: {targetDb}");

        logger.LogInformation("Database {Db} not found; creating", targetDb);
        await using var create = new SqlCommand($"CREATE DATABASE [{targetDb}];", master);
        create.CommandTimeout = 120;
        await create.ExecuteNonQueryAsync(ct);
    }

    private static List<string> SplitGoBatches(string script)
    {
        // Splits on a line whose only non-whitespace content is "GO" — that's what sqlcmd
        // recognises. SqlCommand can't run multi-batch text, so we feed it batch by batch.
        return GoSplitter().Split(script).ToList();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex GoSplitter();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
}
