using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CardVault.Tests.Persistence;

/// <summary>
/// Successor of <c>AddPinKdfColumnsMigrationTests</c> after the Gate 0 / T7 rebaseline.
/// The CardVault chain is a single <c>InitialBaseline</c> migration generated from the current model,
/// so the former per-migration guarantees are restated against the baseline:
/// the PIN-KDF columns (SEC-02 3.3) exist on <c>Cards</c> and are nullable, the baseline is purely
/// additive (it creates, never alters or drops, existing columns), and the real Npgsql migration
/// pipeline (<c>IMigrator.GenerateScript</c>) produces SQL for it. <c>EnsureCreated()</c>, used by the
/// InMemory provider elsewhere in this suite, never runs migrations and would mask a broken one.
/// </summary>
public sealed class InitialBaselineMigrationTests
{
    private static readonly string[] PinKdfColumns = { "PinHashAlgorithm", "PinHashParams", "PinSalt" };

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
    public void Chain_IsExactlyTheInitialBaseline()
    {
        using var db = CreateNpgsqlContext();

        db.Database.GetMigrations().Should().ContainSingle()
            .Which.Should().Be(MigrationBaselineAdoption.BaselineMigrationId);
    }

    [Fact]
    public void Script_CreatesCardsWithNullablePinKdfColumns_AndNeverAltersOrDrops()
    {
        using var db = CreateNpgsqlContext();
        var migrator = db.GetService<IMigrator>();

        var script = migrator.GenerateScript(fromMigration: null, toMigration: MigrationBaselineAdoption.BaselineMigrationId);

        script.Should().Contain("CREATE TABLE \"Cards\"");
        foreach (var column in PinKdfColumns)
        {
            script.Should().Contain($"\"{column}\"");
        }

        script.Should().NotContain("DROP COLUMN");
        script.Should().NotContain("ALTER COLUMN");
        script.Should().NotContain("DROP TABLE");
    }

    [Fact]
    public void Up_CreatesCards_WithNullablePinKdfColumns_AndOnlyCreates()
    {
        var builder = BuildOperations("Up");

        var cards = builder.Operations.OfType<CreateTableOperation>().Should().ContainSingle(op => op.Name == "Cards").Subject;

        var pinColumns = cards.Columns.Where(c => PinKdfColumns.Contains(c.Name)).ToList();
        pinColumns.Select(c => c.Name).Should().BeEquivalentTo(PinKdfColumns, "the SEC-02 PIN-KDF columns are part of the baseline");
        pinColumns.Should().OnlyContain(c => c.IsNullable, "PIN-KDF columns are nullable so pre-KDF rows need no backfill");
        cards.Columns.Should().ContainSingle(c => c.Name == "PinHash").Which.IsNullable.Should().BeTrue();

        // A baseline only creates. Anything else means hand edits crept into the generated file.
        builder.Operations.Should().NotContain(op => op is DropColumnOperation);
        builder.Operations.Should().NotContain(op => op is AlterColumnOperation);
        builder.Operations.Should().NotContain(op => op is RenameColumnOperation);
        builder.Operations.Should().NotContain(op => op is DropTableOperation);
        builder.Operations.Should().OnlyContain(op =>
            op is CreateTableOperation || op is CreateIndexOperation || op is AddForeignKeyOperation
            || op is EnsureSchemaOperation || op is AlterDatabaseOperation);
    }

    [Fact]
    public void Down_DropsEveryTableTheBaselineCreates()
    {
        var created = BuildOperations("Up").Operations.OfType<CreateTableOperation>().Select(op => op.Name).ToList();
        var dropped = BuildOperations("Down").Operations.OfType<DropTableOperation>().Select(op => op.Name).ToList();

        created.Should().NotBeEmpty();
        dropped.Should().BeEquivalentTo(created, "Down() must undo exactly what Up() created");
    }

    private static MigrationBuilder BuildOperations(string method)
    {
        var migration = new InitialBaseline();
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migration, new object[] { builder });

        return builder;
    }
}
