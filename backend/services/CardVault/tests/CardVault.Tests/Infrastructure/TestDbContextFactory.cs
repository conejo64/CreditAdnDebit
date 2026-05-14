using CardVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Infrastructure;

/// <summary>
/// Creates isolated InMemory DbContext instances for testing.
/// Each call to Create() uses a unique database name to prevent cross-test contamination.
/// </summary>
public static class TestDbContextFactory
{
    public static CardVaultDbContext Create(string? databaseName = null)
    {
        var dbName = databaseName ?? $"CardVault_Test_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new CardVaultDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Creates a second context pointing to the same in-memory database.
    /// Useful for testing read-after-write with a clean tracking context.
    /// </summary>
    public static CardVaultDbContext CreateSecondContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new CardVaultDbContext(options);
    }
}
