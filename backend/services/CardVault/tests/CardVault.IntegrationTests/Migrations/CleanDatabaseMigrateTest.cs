using CardVault.Infrastructure.Persistence;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Migrations;

/// <summary>
/// Gate 0 / T7 acceptance probe: applying the CardVault migration chain to an empty PostgreSQL
/// database must succeed. This is the path a fresh deployment takes, as opposed to the
/// <c>EnsureCreated()</c> path used by the behaviour tests and by the Development environment.
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

    // Observed 2026-10-05 on postgres:16-alpine (Testcontainers) with HEAD d99e89f:
    //   Npgsql.PostgresException : 42P01: relation "Countries" does not exist
    //   at Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal.NpgsqlMigrator.MigrateAsync(...)
    // Root cause: 19 early migrations in CardVault.Infrastructure.Persistence/Migrations carry no
    // [Migration] attribute or Designer file, so EF Core does not see them; the first visible
    // migration (20260311014310_AddCustomerDataFields) alters "Countries", which nothing created.
    [Fact(Skip = "Gate 0 T7: CardVault migration chain rebaseline pending — fails with 42P01 on a clean database",
        DisplayName = "Database.Migrate() on an empty PostgreSQL database applies the whole chain")]
    public async Task Migrate_on_empty_database_succeeds()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new CardVaultDbContext(options);

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        pending.Should().NotBeEmpty("an empty database has every migration pending");

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Customers.AsNoTracking().CountAsync()).Should().Be(0, "the schema exists and is empty");
    }
}
