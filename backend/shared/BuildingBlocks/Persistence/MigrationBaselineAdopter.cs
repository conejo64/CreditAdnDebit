using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Startup helper that lets a PostgreSQL database created with <c>EnsureCreated()</c> (schema present,
/// no <c>__EFMigrationsHistory</c> rows) adopt a single baseline migration without running its
/// <c>Up()</c>, which would fail with <c>42P07 relation already exists</c>.
///
/// Provider-agnostic in signature, PostgreSQL-only in behaviour: every service in this platform that
/// collapsed its migration chain into one generated baseline (Gate 0 / T7 CardVault, T8 IsoSwitch)
/// wraps this with its own baseline ID, probe table and runbook.
/// </summary>
public static class MigrationBaselineAdopter
{
    /// <summary>EF Core provider name reported by <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>.</summary>
    public const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Marks <paramref name="baselineMigrationId"/> as applied when, and only when, the database already
    /// holds the application schema (<paramref name="schemaProbeTable"/> exists) and has no applied
    /// migration. Returns <c>true</c> when a history row was inserted. Idempotent: a database with any
    /// applied migration, or with no schema, is left untouched.
    /// </summary>
    /// <remarks>
    /// Adoption is refused (logged as an error, returns <c>false</c>) when the assembly holds more than
    /// the baseline migration: an <c>EnsureCreated()</c> database matches the <em>current</em> model, so
    /// recording only the baseline would make the later migrations fail. That case is a human decision,
    /// documented in <paramref name="runbookPath"/>.
    /// </remarks>
    public static async Task<bool> TryAdoptBaselineAsync(
        DbContext db,
        string baselineMigrationId,
        string schemaProbeTable,
        string runbookPath,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineMigrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaProbeTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(runbookPath);
        ArgumentNullException.ThrowIfNull(logger);

        var database = db.Database;
        if (!string.Equals(database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal))
        {
            return false;
        }

        var migrations = database.GetMigrations().ToList();
        if (migrations.Count != 1 || migrations[0] != baselineMigrationId)
        {
            logger.LogError(
                "Baseline adoption skipped: the migration chain is no longer the single baseline ({Count} migrations found). " +
                "An EnsureCreated() database must be adopted by hand; see {Runbook}.",
                migrations.Count, runbookPath);
            return false;
        }

        if (!await database.CanConnectAsync(ct))
        {
            return false;
        }

        var applied = await database.GetAppliedMigrationsAsync(ct);
        if (applied.Any())
        {
            return false;
        }

        if (!await TableExistsAsync(database, schemaProbeTable, ct))
        {
            return false;
        }

        var history = db.GetService<IHistoryRepository>();
        var productVersion = ProductInfo.GetVersion();

        await database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
        await database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baselineMigrationId, productVersion)), ct);

        logger.LogWarning(
            "Baseline adoption: database already had the application schema (table {Probe} exists) with no migration history; " +
            "recorded {MigrationId} (ProductVersion {ProductVersion}) as applied without executing it.",
            schemaProbeTable, baselineMigrationId, productVersion);

        return true;
    }

    private static async Task<bool> TableExistsAsync(DatabaseFacade database, string tableName, CancellationToken ct)
    {
        var connection = database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await database.OpenConnectionAsync(ct);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables " +
                "WHERE table_schema = current_schema() AND table_name = @table)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(ct);
            return result is true;
        }
        finally
        {
            if (opened)
            {
                await database.CloseConnectionAsync();
            }
        }
    }
}
