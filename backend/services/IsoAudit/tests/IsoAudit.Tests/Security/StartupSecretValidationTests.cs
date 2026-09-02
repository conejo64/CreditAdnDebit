using IsoAudit.Tests.Infrastructure;

namespace IsoAudit.Tests.Security;

/// <summary>
/// Verifies SEC-3 at the composition root: IsoAudit refuses to start when Jwt:Key is
/// absent, empty, a known DEV placeholder, or shorter than 32 characters.
/// </summary>
/// <remarks>
/// These tests own exactly one property — that <see cref="Api.Security.JwtOptionsValidator"/>
/// is actually wired into startup, so a bad key stops the host instead of being ignored.
/// They deliberately assert only that startup fails, not which exception carries the
/// failure.
///
/// WebApplicationFactory runs a top-level-statements app on a deferred host. When startup
/// fails, DeferredHost.StartAsync resolves services from a provider that the failure is
/// concurrently disposing, so under load ObjectDisposedException surfaces in place of the
/// OptionsValidationException that ValidateOnStart raised. Both mean the host refused to
/// start, which is the property here; pinning the exact type made these tests fail
/// whenever the solution's other test assemblies ran hot enough, and no parallelism
/// setting fixes that because the trigger is machine load.
///
/// Nothing is lost by relaxing it. Which rule rejected a key is covered exhaustively and
/// deterministically by <see cref="JwtOptionsValidatorTests"/>, and that
/// OptionsValidationException is what ValidateOnStart throws is a framework guarantee, not
/// IsoAudit behaviour. <see cref="IsoAudit_ValidJwtKey_StartsSuccessfully"/> is the
/// counterpart that keeps these assertions honest: it proves a good key does start the
/// host, so "startup failed" can never pass vacuously.
/// </remarks>
public class StartupSecretValidationTests
{
    private static void AssertStartupIsRefused(string jwtKey)
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:Key", jwtKey));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
    }

    // ── Missing key ──────────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_MissingJwtKey_ThrowsOnStart()
    {
        AssertStartupIsRefused(string.Empty);
    }

    // ── DEV placeholder ──────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_DevPlaceholderJwtKey_ThrowsOnStart()
    {
        AssertStartupIsRefused("DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM");
    }

    // ── Short key ────────────────────────────────────────────────────────────

    [Fact]
    public void IsoAudit_ShortJwtKey_ThrowsOnStart()
    {
        AssertStartupIsRefused("tooshort");
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
