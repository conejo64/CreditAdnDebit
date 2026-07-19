using IsoSwitch.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// Verifies SEC-05/SEC-11: IsoSwitch.Api fails startup fast — before accepting
/// any HTTP traffic — when the admin API key is absent, empty, or equals the
/// known DEV placeholder literal "dev-admin-key". Mirrors the
/// TokenizationOptionsValidator / JwtOptionsValidator fail-fast pattern already
/// established in this codebase.
/// </summary>
public class AdminApiKeyStartupTests
{
    // ── Missing key ──────────────────────────────────────────────────────────

    [Fact]
    public void MissingAdminApiKey_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Admin:ApiKey", string.Empty);
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── DEV placeholder key ──────────────────────────────────────────────────

    [Fact]
    public void DevPlaceholderAdminApiKey_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Admin:ApiKey", "dev-admin-key");
            });

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Admin:ApiKey", ex.Message);
    }

    // ── Valid operator-supplied key ──────────────────────────────────────────

    [Fact]
    public void ValidOperatorSuppliedAdminApiKey_StartsSuccessfully()
    {
        // The default IsoSwitchWebApplicationFactory already provides a valid key.
        using var factory = new IsoSwitchWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
