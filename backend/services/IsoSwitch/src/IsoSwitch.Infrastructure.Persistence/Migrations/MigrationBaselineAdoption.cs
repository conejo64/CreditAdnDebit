using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;

namespace IsoSwitch.Infrastructure.Persistence.Migrations;

/// <summary>
/// IsoSwitch binding of <see cref="MigrationBaselineAdopter"/>: lets a database created with
/// <c>EnsureCreated()</c> (schema present, no <c>__EFMigrationsHistory</c> rows) adopt
/// <see cref="BaselineMigrationId"/> without running its <c>Up()</c>, which would fail with
/// <c>42P07 relation "AuditEvents" already exists</c>.
/// See <c>docs/runbooks/isoswitch-migration-baseline.md</c>.
/// </summary>
public static class MigrationBaselineAdoption
{
    /// <summary>The single migration the IsoSwitch chain was collapsed into (Gate 0 / T8).</summary>
    public const string BaselineMigrationId = "20261006185159_InitialBaseline";

    /// <summary>
    /// A table that only exists once the application schema has been created. Its presence, together
    /// with an empty migration history, is what identifies an <c>EnsureCreated()</c> database.
    /// </summary>
    private const string SchemaProbeTable = "Transactions";

    private const string Runbook = "docs/runbooks/isoswitch-migration-baseline.md";

    /// <summary>
    /// Marks the baseline as applied when, and only when, the database already holds the application
    /// schema and has no applied migration. Returns <c>true</c> when a history row was inserted.
    /// Idempotent; refuses (with an error log) once the chain holds more than the baseline.
    /// </summary>
    public static Task<bool> TryAdoptBaselineAsync(IsoSwitchDbContext db, ILogger logger, CancellationToken ct = default)
        => MigrationBaselineAdopter.TryAdoptBaselineAsync(db, BaselineMigrationId, SchemaProbeTable, Runbook, logger, ct);
}
