using System.Net;
using System.Text.Json;
using BuildingBlocks.Security;
using CardVault.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CardVault.Tests.Security;

/// <summary>
/// G1-T5 at the composition root: CardVault refuses to start when its ES256 signing key is
/// absent, when a public key is handed to it in the private slot (or the reverse), when the
/// configured public key does not pair with the private key, or when the retired HS256
/// <c>Jwt:SigningKey</c> is still present. The valid-pair and ephemeral-pair cases keep the
/// refusals honest by proving a correct configuration does start the host.
/// </summary>
/// <remarks>
/// Only "startup is refused" is asserted here, not the exception type: WebApplicationFactory
/// runs a deferred host and under load an ObjectDisposedException can surface in place of the
/// OptionsValidationException that ValidateOnStart raised. Which rule rejected the configuration
/// is pinned deterministically by <see cref="JwtOptionsValidatorTests"/>.
/// </remarks>
public class StartupSecretValidationTests
{
    private static void AssertStartupIsRefused(Action<IWebHostBuilder> configure)
    {
        using var factory = new CardVaultWebApplicationFactory().WithWebHostBuilder(configure);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
    }

    [Fact]
    public void CardVault_MissingPrivateKey_RefusesToStart()
    {
        AssertStartupIsRefused(b => b.UseSetting("Jwt:PrivateKeyPem", string.Empty));
    }

    [Fact]
    public void CardVault_PublicKeyInThePrivateKeySlot_RefusesToStart()
    {
        AssertStartupIsRefused(b => b.UseSetting("Jwt:PrivateKeyPem", CardVaultWebApplicationFactory.PublicKeyPem));
    }

    [Fact]
    public void CardVault_PrivateKeyInThePublicKeySlot_RefusesToStart()
    {
        AssertStartupIsRefused(b => b.UseSetting("Jwt:PublicKeyPem", CardVaultWebApplicationFactory.PrivateKeyPem));
    }

    [Fact]
    public void CardVault_PublicKeyThatDoesNotMatchThePrivateKey_RefusesToStart()
    {
        var other = EcdsaKeyMaterial.GenerateKeyPair();
        AssertStartupIsRefused(b => b.UseSetting("Jwt:PublicKeyPem", other.PublicKeyPem));
    }

    [Fact]
    public void CardVault_LegacySymmetricSigningKeyStillConfigured_RefusesToStart()
    {
        AssertStartupIsRefused(b => b.UseSetting("Jwt:SigningKey", "TestSigningKeyForCardVaultIntegrationTests"));
    }

    [Fact]
    public void CardVault_ValidKeyPair_StartsSuccessfully()
    {
        // The default CardVaultWebApplicationFactory already provides a valid pair.
        using var factory = new CardVaultWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }

    [Fact]
    public async Task CardVault_EphemeralKeysInDevelopment_StartsAndPublishesAGeneratedPublicKey()
    {
        using var factory = new CardVaultWebApplicationFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Jwt:PrivateKeyPem", string.Empty);
            b.UseSetting("Jwt:PublicKeyPem", string.Empty);
            b.UseSetting("Jwt:DevelopmentEphemeralKeys", "true");
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = body.RootElement.GetProperty("keys")[0];
        Assert.Equal("EC", key.GetProperty("kty").GetString());
        // A throwaway pair is never the test pair.
        var testKid = EcdsaKeyMaterial.ComputeKeyId(EcdsaKeyMaterial.ImportPublicKey(CardVaultWebApplicationFactory.PublicKeyPem));
        Assert.NotEqual(testKid, key.GetProperty("kid").GetString());
    }

    [Fact]
    public void CardVault_EphemeralKeysOutsideDevelopment_RefusesToStart()
    {
        AssertStartupIsRefused(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Jwt:PrivateKeyPem", string.Empty);
            b.UseSetting("Jwt:PublicKeyPem", string.Empty);
            b.UseSetting("Jwt:DevelopmentEphemeralKeys", "true");
        });
    }
}
