using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Migrations;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Migrations;

/// <summary>
/// Gate 0 / T7 acceptance: applying the CardVault migration chain to an empty PostgreSQL database
/// must succeed. This is the path a fresh deployment takes, as opposed to the <c>EnsureCreated()</c>
/// path used by the behaviour tests and by the Development environment.
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

    // History: observed 2026-10-05 on postgres:16-alpine with HEAD 8eaa1bd:
    //   Npgsql.PostgresException : 42P01: relation "Countries" does not exist
    // 19 early migrations carried no [Migration] attribute or Designer file, so EF Core never saw
    // them; the first visible migration altered "Countries", which nothing had created. The chain
    // was collapsed into a single InitialBaseline migration generated from the current model.
    [Fact(DisplayName = "Database.Migrate() on an empty PostgreSQL database applies the whole chain")]
    public async Task Migrate_on_empty_database_succeeds()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new CardVaultDbContext(options);

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        pending.Should().NotBeEmpty("an empty database has every migration pending");

        await db.Database.MigrateAsync();

        var historyRows = await db.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"")
            .ToListAsync();
        historyRows.Should().ContainSingle("the chain is a single baseline migration")
            .Which.Should().Be(MigrationBaselineAdoption.BaselineMigrationId);

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
            "the model snapshot matches the model: no migration is pending after the baseline");

        (await db.Customers.AsNoTracking().CountAsync()).Should().Be(0, "the schema exists and is empty");
    }
}
