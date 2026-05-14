using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Infrastructure.Identity.Auth;

public sealed class IdentityAppDbContext : IdentityDbContext<AppUser>
{
    public IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).IsRequired();
            b.Property(x => x.TokenHash).IsRequired();
            b.HasIndex(x => new { x.UserId, x.TokenHash }).IsUnique();
        });
    }
}