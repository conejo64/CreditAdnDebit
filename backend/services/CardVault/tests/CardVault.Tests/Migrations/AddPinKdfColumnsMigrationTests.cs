using CardVault.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CardVault.Tests.Migrations;

/// <summary>
/// SEC-02 (3.3): proves the `AddPinKdfColumns` migration is additive, nullable, and produces
/// no data loss against the pre-migration schema.
/// `CardVaultDbContext` uses `Database.Migrate()` in Production — `EnsureCreated()` (used by
/// the InMemory test provider elsewhere in this suite) never runs migrations, so this test
/// exercises the real Npgsql migration pipeline (`IMigrator.GenerateScript`) directly against
/// the generated migration class rather than relying on InMemory, which would silently mask a
/// broken migration.
/// </summary>
public sealed class AddPinKdfColumnsMigrationTests
{
    private static CardVaultDbContext CreateNpgsqlContext()
    {
        // A real connection is never opened for script generation — Npgsql only needs a
        // syntactically valid connection string to build its SQL-generation services.
        var options = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseNpgsql("Host=localhost;Database=cardvault_migration_test;Username=test;Password=test")
            .Options;
        return new CardVaultDbContext(options);
    }

    [Fact]
    public void Migration_AddsThreeNullableColumns_ToCardsTable()
    {
        using var db = CreateNpgsqlContext();
        var migrator = db.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            fromMigration: "20260602231307_AddEncryptedNotificationDestination",
            toMigration: "20260712143908_AddPinKdfColumns");

        script.Should().Contain("\"PinHashAlgorithm\"");
        script.Should().Contain("\"PinHashParams\"");
        script.Should().Contain("\"PinSalt\"");
        script.Should().Contain("\"Cards\"");

        // Additive only — no destructive operations against any existing column.
        script.Should().NotContain("DROP COLUMN");
        script.Should().NotContain("ALTER COLUMN \"PinHash\"");
    }

    [Fact]
    public void Migration_UpOperations_AreAllAdditiveAndNullable_NoDataLoss()
    {
        var migration = new CardVault.Infrastructure.Persistence.Migrations.AddPinKdfColumns();
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.GetType()
            .GetMethod("Up", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migration, new object[] { builder });

        var addColumnOps = builder.Operations.OfType<AddColumnOperation>().ToList();

        addColumnOps.Should().HaveCount(3, "the migration must add exactly PinHashAlgorithm, PinHashParams, PinSalt");
        addColumnOps.Should().OnlyContain(op => op.Table == "Cards");
        addColumnOps.Should().OnlyContain(op => op.IsNullable, "all new PIN-KDF columns must be nullable so existing rows need no backfill");

        var columnNames = addColumnOps.Select(op => op.Name).ToList();
        columnNames.Should().BeEquivalentTo(new[] { "PinHashAlgorithm", "PinHashParams", "PinSalt" });

        // No operation in Up() drops, renames, or alters an existing column — proves the
        // pre-existing PinHash values are left completely untouched.
        builder.Operations.Should().NotContain(op => op is DropColumnOperation);
        builder.Operations.Should().NotContain(op => op is AlterColumnOperation);
        builder.Operations.Should().NotContain(op => op is RenameColumnOperation);
    }

    [Fact]
    public void Migration_DownOperations_OnlyDropTheThreeAddedColumns()
    {
        var migration = new CardVault.Infrastructure.Persistence.Migrations.AddPinKdfColumns();
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.GetType()
            .GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migration, new object[] { builder });

        var dropColumnOps = builder.Operations.OfType<DropColumnOperation>().ToList();
        dropColumnOps.Should().HaveCount(3);
        dropColumnOps.Select(op => op.Name).Should()
            .BeEquivalentTo(new[] { "PinHashAlgorithm", "PinHashParams", "PinSalt" });
        dropColumnOps.Should().OnlyContain(op => op.Table == "Cards");
    }
}
