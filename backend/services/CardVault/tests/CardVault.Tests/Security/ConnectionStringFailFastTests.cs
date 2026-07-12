using CardVault.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Security;

/// <summary>
/// TDD tests (RED before SEC-01/SEC-9 implementation): CardVault.Api SHALL fail
/// fast at startup — before accepting any HTTP traffic — when a required
/// secret-bearing connection string is absent from all configuration sources,
/// with an error message that references the missing configuration key.
/// Satisfies security-hardening SEC-9 scenario "Missing required secret env var
/// causes fail-fast startup".
/// </summary>
public class ConnectionStringFailFastTests
{
    // ── Missing Postgres connection string ──────────────────────────────────

    [Fact]
    public void CardVault_MissingPostgresConnectionString_ThrowsOnStart()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:Postgres", string.Empty);
            });

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:Postgres", ex.Message);
    }

    // ── Missing SqlServerIdentity connection string ─────────────────────────

    [Fact]
    public void CardVault_MissingSqlServerIdentityConnectionString_ThrowsOnStart()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:SqlServerIdentity", string.Empty);
            });

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ConnectionStrings:SqlServerIdentity", ex.Message);
    }

    // ── Valid connection strings (factory defaults) ─────────────────────────

    [Fact]
    public void CardVault_ValidConnectionStrings_StartsSuccessfully()
    {
        // The default CardVaultWebApplicationFactory already provides valid test
        // connection strings (required so DbContext construction itself succeeds).
        using var factory = new CardVaultWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
