using IsoAudit.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace IsoAudit.Tests.Security;

/// <summary>
/// Verifies SEC-3: IsoAudit refuses to start when Jwt:Key is absent,
/// empty, a known DEV placeholder, or shorter than 32 characters.
/// </summary>
public class StartupSecretValidationTests
{
    // ── Missing key ──────────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_MissingJwtKey_ThrowsOnStart()
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:Key", string.Empty);
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── DEV placeholder ──────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_DevPlaceholderJwtKey_ThrowsOnStart()
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:Key", "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Short key ────────────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_ShortJwtKey_ThrowsOnStart()
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Jwt:Key", "tooshort");
            });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    // ── Valid key ────────────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_ValidJwtKey_StartsSuccessfully()
    {
        // IsoAuditWebApplicationFactory provides a valid key by default.
        using var factory = new IsoAuditWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
