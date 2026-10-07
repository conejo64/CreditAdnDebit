using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;

namespace CardVault.IntegrationTests.Infrastructure;

/// <summary>
/// Single xunit collection so all integration test classes share one PostgreSQL server
/// (one container start per run) while each test still gets its own database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>
/// Trait values used to separate integration tests from the solution-wide unit test run.
/// </summary>
public static class Category
{
    public const string TraitName = "Category";
    public const string Integration = "Integration";
}

/// <summary>
/// Base class for behaviour tests that need a <see cref="CardVaultDbContext"/> on a fresh
/// PostgreSQL database. xunit creates one instance per test, so every test owns its database.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Fixture;
    protected CardVaultDbContext Db = null!;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;
    }

    public virtual async Task InitializeAsync()
    {
        Db = await Fixture.CreateDbContextAsync();
        OnDbReady();
    }

    /// <summary>Hook for subclasses to build their services once <see cref="Db"/> exists.</summary>
    protected virtual void OnDbReady()
    {
    }

    public virtual async Task DisposeAsync()
    {
        // Db stays null when InitializeAsync failed (no server, container start error, ...).
        // Guarding here lets the real infrastructure error surface instead of a NullReferenceException.
        if (Db is not null)
        {
            await Db.DisposeAsync();
        }
    }

    /// <summary>
    /// Seeds a customer and a credit account. Values respect the column constraints that PostgreSQL
    /// enforces and the InMemory provider ignores: <c>CustomerNumber</c> max 32, <c>DocumentId</c> max 20,
    /// <c>Phone</c> max 20, <c>Email</c> max 80.
    /// </summary>
    protected async Task<CardAccountEntity> SeedCreditAccountAsync(
        decimal creditLimit = 1000m,
        decimal? availableLimit = null,
        string productCode = "TEST_PROD")
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        Db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}"[..8],
            FullName = "Integration Test Customer",
            DocumentId = $"DOC{customerId:N}"[..10],
            Email = "integration@test.com",
            Phone = "+593999000099",
            CreatedOn = DateTimeOffset.UtcNow
        });

        var account = Db.Accounts.Add(new CardAccountEntity
        {
            Id = accountId,
            CustomerId = customerId,
            AccountNumber = $"ACC{accountId:N}"[..10],
            AccountType = AccountType.Credit,
            ProductCode = productCode,
            CreditLimit = creditLimit,
            AvailableLimit = availableLimit ?? creditLimit,
            CurrencyCode = "USD",
            Status = AccountStatus.Active,
            CreatedOn = DateTimeOffset.UtcNow
        }).Entity;

        await Db.SaveChangesAsync();
        return account;
    }
}
