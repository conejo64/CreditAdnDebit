using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardVault.Infrastructure.Identity.Auth;

public sealed class IdentityAppDbContextFactory : IDesignTimeDbContextFactory<IdentityAppDbContext>
{
    public IdentityAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityAppDbContext>();

        var cs = Environment.GetEnvironmentVariable("CARDVAULT_SQLSERVER")
                 ?? "Server=localhost,11433;Database=CardVaultIdentity;User Id=sa;Password=Your_strong_Passw0rd!;TrustServerCertificate=true";

        options.UseSqlServer(cs);
        return new IdentityAppDbContext(options.Options);
    }
}
