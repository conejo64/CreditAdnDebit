using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace CardVault.Infrastructure.Persistence.Migrations;

/// <summary>
/// Startup helper that lets a database created with <c>EnsureCreated()</c> (schema present, no
/// <c>__EFMigrationsHistory</c> rows) adopt the <see cref="BaselineMigrationId"/> without running its
/// <c>Up()</c>, which would fail with <c>42P07 relation already exists</c>.
/// See <c>docs/runbooks/cardvault-migration-baseline.md</c>.
/// </summary>
public static class MigrationBaselineAdoption
{
    /// <summary>The single migration the CardVault chain was collapsed into (Gate 0 / T7).</summary>
    public const string BaselineMigrationId = "20261006152735_InitialBaseline";

    /// <summary>
    /// A table that only exists once the application schema has been created. Its presence, together
    /// with an empty migration history, is what identifies an <c>EnsureCreated()</c> database.
    /// </summary>
    private const string SchemaProbeTable = "Customers";

    /// <summary>
    /// Marks the baseline as applied when, and only when, the database already holds the application
    /// schema and has no applied migration. Returns <c>true</c> when a history row was inserted.
    /// Idempotent: a database with any applied migration, or with no schema, is left untouched.
    /// </summary>
    /// <remarks>
    /// Adoption is refused (logged as an error, returns <c>false</c>) when the assembly holds more than
    /// the baseline migration: an <c>EnsureCreated()</c> database matches the <em>current</em> model, so
    /// recording only the baseline would make the later migrations fail. That case is a human decision.
    /// </remarks>
    public static async Task<bool> TryAdoptBaselineAsync(CardVaultDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var database = db.Database;
        if (!database.IsNpgsql())
        {
            return false;
        }

        var migrations = database.GetMigrations().ToList();
        if (migrations.Count != 1 || migrations[0] != BaselineMigrationId)
        {
            logger.LogError(
                "Baseline adoption skipped: the migration chain is no longer the single baseline ({Count} migrations found). " +
                "An EnsureCreated() database must be adopted by hand; see docs/runbooks/cardvault-migration-baseline.md.",
                migrations.Count);
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

        if (!await TableExistsAsync(database, SchemaProbeTable, ct))
        {
            return false;
        }

        var history = db.GetService<IHistoryRepository>();
        var productVersion = ProductInfo.GetVersion();

        await database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
        await database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(BaselineMigrationId, productVersion)), ct);

        logger.LogWarning(
            "Baseline adoption: database already had the application schema (table {Probe} exists) with no migration history; " +
            "recorded {MigrationId} (ProductVersion {ProductVersion}) as applied without executing it.",
            SchemaProbeTable, BaselineMigrationId, productVersion);

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
