using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Migrations;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardVault.IntegrationTests.Migrations;

/// <summary>
/// Gate 0 / T7: a database provisioned with <c>EnsureCreated()</c> (the Development path) has the
/// whole schema but no <c>__EFMigrationsHistory</c> table. Running <c>Migrate()</c> on it would try to
/// create every table again. <see cref="MigrationBaselineAdoption"/> inserts the baseline history row
/// instead, after which <c>Migrate()</c> is a no-op and future migrations apply normally.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public sealed class BaselineAdoptionTest
{
    private readonly PostgresFixture _fixture;

    public BaselineAdoptionTest(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "An EnsureCreated database adopts the baseline and then migrates without error")]
    public async Task EnsureCreated_database_adopts_the_baseline_then_migrates_cleanly()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new CardVaultDbContext(options);

        await db.Database.EnsureCreatedAsync();
        (await db.Database.GetAppliedMigrationsAsync()).Should().BeEmpty("EnsureCreated never writes migration history");

        var adopted = await MigrationBaselineAdoption.TryAdoptBaselineAsync(db, NullLogger.Instance);
        adopted.Should().BeTrue("the schema exists and no migration is recorded");

        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        applied.Should().ContainSingle().Which.Should().Be(MigrationBaselineAdoption.BaselineMigrationId);
        (await CountHistoryRowsAsync(db)).Should().Be(1);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        var again = await MigrationBaselineAdoption.TryAdoptBaselineAsync(db, NullLogger.Instance);
        again.Should().BeFalse("adoption is idempotent once a migration is recorded");
        (await CountHistoryRowsAsync(db)).Should().Be(1);
    }

    [Fact(DisplayName = "An empty database is not adopted; Migrate() owns it")]
    public async Task Empty_database_is_left_to_migrate()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new CardVaultDbContext(options);

        var adopted = await MigrationBaselineAdoption.TryAdoptBaselineAsync(db, NullLogger.Instance);

        adopted.Should().BeFalse("there is no schema to adopt");
        (await db.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();

        await db.Database.MigrateAsync();
        (await CountHistoryRowsAsync(db)).Should().Be(1);
    }

    private static async Task<int> CountHistoryRowsAsync(CardVaultDbContext db)
    {
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"__EFMigrationsHistory\"")
            .ToListAsync();
        return count.Single();
    }
}
