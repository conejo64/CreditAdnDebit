using IsoAudit.Tests.Infrastructure;

namespace IsoAudit.Tests.Security;

/// <summary>
/// Verifies G1-T5 at the composition root: IsoAudit refuses to start when the CardVault
/// ES256 public key (<c>Jwt:PublicKeyPem</c>) is absent or malformed, when a private key is
/// handed to it instead, or when the retired HS256 <c>Jwt:Key</c> is still configured.
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
/// IsoAudit behaviour. <see cref="IsoAudit_ValidPublicKey_StartsSuccessfully"/> is the
/// counterpart that keeps these assertions honest: it proves a good key does start the
/// host, so "startup failed" can never pass vacuously.
/// </remarks>
public class StartupSecretValidationTests
{
    private static void AssertStartupIsRefused(string setting, string value)
    {
        using var factory = new IsoAuditWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting(setting, value));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
    }

    [Fact]
    public void IsoAudit_MissingPublicKey_ThrowsOnStart()
    {
        AssertStartupIsRefused("Jwt:PublicKeyPem", string.Empty);
    }

    [Fact]
    public void IsoAudit_PrivateKeyInThePublicSlot_ThrowsOnStart()
    {
        AssertStartupIsRefused("Jwt:PublicKeyPem", IsoAuditWebApplicationFactory.PrivateKeyPem);
    }

    [Fact]
    public void IsoAudit_MalformedPublicKey_ThrowsOnStart()
    {
        AssertStartupIsRefused("Jwt:PublicKeyPem", "-----BEGIN PUBLIC KEY-----\nnot-a-key\n-----END PUBLIC KEY-----");
    }

    [Fact]
    public void IsoAudit_RetiredSymmetricKeyStillConfigured_ThrowsOnStart()
    {
        AssertStartupIsRefused("Jwt:Key", "TestJwtKeyForIsoAuditServiceTests32Plus");
    }

    [Fact]
    public void IsoAudit_ValidPublicKey_StartsSuccessfully()
    {
        // IsoAuditWebApplicationFactory provides a valid public key by default.
        using var factory = new IsoAuditWebApplicationFactory();
        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }
}
