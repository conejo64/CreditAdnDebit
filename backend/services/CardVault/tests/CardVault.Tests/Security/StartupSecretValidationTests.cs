using CardVault.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Security;

/// <summary>
/// Verifies SEC-2: CardVault refuses to start when Jwt:SigningKey is absent,
/// empty, a known DEV placeholder, or shorter than 32 characters.
/// Uses inline WebApplicationFactory overrides so each test boots in isolation.
/// </summary>
public class StartupSecretValidationTests
{
    private static CardVaultWebApplicationFactory BuildFactory(
        Action<IWebHostBuilder> configure)
    {
        var factory = new CardVaultWebApplicationFactory();
        return factory.WithWebHostBuilder(configure) as CardVaultWebApplicationFactory
               ?? throw new InvalidOperationException("WithWebHostBuilder returned unexpected type.");
    }

    // ── Missing key ──────────────────────────────────────────────────────────

    [Fact]
    public void CardVault_MissingJwtSigningKey_ThrowsOnStart()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:SigningKey", string.Empty);
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── DEV placeholder key ──────────────────────────────────────────────────

    [Fact]
    public void CardVault_DevPlaceholderSigningKey_ThrowsOnStart()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:SigningKey", "DEV_ONLY_change_me_please_32+chars");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Key shorter than 32 chars ────────────────────────────────────────────

    [Fact]
    public void CardVault_ShortSigningKey_ThrowsOnStart()
    {
        using var factory = new CardVaultWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:SigningKey", "tooshort");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Valid key ────────────────────────────────────────────────────────────

    [Fact]
    public void CardVault_ValidSigningKey_StartsSuccessfully()
    {
        // The default CardVaultWebApplicationFactory already provides a valid key.
        using var factory = new CardVaultWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
