using FluentAssertions;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Migrations;
using IsoSwitch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.IntegrationTests.Migrations;

/// <summary>
/// Gate 0 / T8 acceptance: applying the IsoSwitch migration chain to an empty PostgreSQL database
/// must succeed and must leave the model, the snapshot and the history in agreement. This is the path
/// a fresh deployment takes, as opposed to the <c>EnsureCreated()</c> path used by Development.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public sealed class CleanDatabaseMigrateTest
{
    private readonly PostgresFixture _fixture;

    public CleanDatabaseMigrateTest(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // History: observed 2026-10-06 on postgres:16-alpine with HEAD 364a091 (old chain): this test
    // passed, because EF Core only discovered FinalMigration (the 12 earlier files had no [Migration]
    // attribute) and that one migration happened to create the whole schema. The chain was still
    // replaced: twelve dead files, and an EnsureCreated() database failed with 42P07 (see
    // BaselineAdoptionTest). This test now pins the generated baseline by ID.
    [Fact(DisplayName = "Database.Migrate() on an empty PostgreSQL database applies exactly one baseline and matches the model")]
    public async Task Migrate_on_empty_database_succeeds()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new IsoSwitchDbContext(options);

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        pending.Should().ContainSingle("the assembly holds exactly the baseline")
            .Which.Should().Be(MigrationBaselineAdoption.BaselineMigrationId);

        await db.Database.MigrateAsync();

        var historyRows = await db.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"")
            .ToListAsync();
        historyRows.Should().ContainSingle("the chain is a single baseline migration")
            .Which.Should().Be(MigrationBaselineAdoption.BaselineMigrationId);

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
            "every migration in the assembly was applied");

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "the snapshot must describe the current model; Program.cs no longer silences PendingModelChangesWarning");

        (await db.Transactions.AsNoTracking().CountAsync()).Should().Be(0, "the schema exists and is empty");
    }
}
