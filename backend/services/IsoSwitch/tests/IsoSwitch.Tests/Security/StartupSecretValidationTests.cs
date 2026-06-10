using IsoSwitch.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// Verifies SEC-1: IsoSwitch refuses to start when Tokenization:Secret is absent,
/// empty, a known DEV placeholder, or shorter than 32 characters.
/// </summary>
public class StartupSecretValidationTests
{
    // ── Missing secret ───────────────────────────────────────────────────────

    [Fact]
    public void IsoSwitch_MissingTokenizationSecret_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Tokenization:Secret", string.Empty);
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── DEV placeholder ──────────────────────────────────────────────────────

    [Fact]
    public void IsoSwitch_DevPlaceholderSecret_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Tokenization:Secret", "DEV_ONLY_CHANGE_ME");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Short secret ─────────────────────────────────────────────────────────

    [Fact]
    public void IsoSwitch_ShortTokenizationSecret_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Tokenization:Secret", "tooshort");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Valid secret ─────────────────────────────────────────────────────────

    [Fact]
    public void IsoSwitch_ValidTokenizationSecret_StartsSuccessfully()
    {
        // IsoSwitchWebApplicationFactory provides a valid secret by default.
        using var factory = new IsoSwitchWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
