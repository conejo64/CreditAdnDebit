using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;

namespace CardVault.Infrastructure.Persistence.Migrations;

/// <summary>
/// CardVault binding of <see cref="MigrationBaselineAdopter"/>: lets a database created with
/// <c>EnsureCreated()</c> (schema present, no <c>__EFMigrationsHistory</c> rows) adopt
/// <see cref="BaselineMigrationId"/> without running its <c>Up()</c>.
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

    private const string Runbook = "docs/runbooks/cardvault-migration-baseline.md";

    /// <summary>
    /// Marks the baseline as applied when, and only when, the database already holds the application
    /// schema and has no applied migration. Returns <c>true</c> when a history row was inserted.
    /// Idempotent; refuses (with an error log) once the chain holds more than the baseline.
    /// </summary>
    public static Task<bool> TryAdoptBaselineAsync(CardVaultDbContext db, ILogger logger, CancellationToken ct = default)
        => MigrationBaselineAdopter.TryAdoptBaselineAsync(db, BaselineMigrationId, SchemaProbeTable, Runbook, logger, ct);
}
