using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardVault.Infrastructure.Persistence;

public sealed class CardVaultDbContextFactory : IDesignTimeDbContextFactory<CardVaultDbContext>
{
    public CardVaultDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CardVaultDbContext>();

        // For design-time migrations. Override via env var if desired.
        var cs = Environment.GetEnvironmentVariable("CARDVAULT_POSTGRES")
                 ?? "Host=localhost;Port=5432;Database=cardvault;Username=postgres;Password=postgres";

        options.UseNpgsql(cs);
        return new CardVaultDbContext(options.Options);
    }
}
