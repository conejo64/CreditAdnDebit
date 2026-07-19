using IsoSwitch.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// Verifies SEC-04/SEC-10: IsoSwitch.Api fails startup fast — before accepting
/// any HTTP traffic — when the ISO 8583 TCP channel would run in Production
/// with TLS disabled against a non-loopback acquirer host. Plaintext stays
/// permitted for loopback hosts, and only outside Production. DNS resolution
/// failures fail closed (treated as non-loopback, TLS still required).
/// Mirrors the TokenizationOptionsValidator / JwtOptionsValidator fail-fast
/// pattern already covered end-to-end by StartupSecretValidationTests.
/// </summary>
public class TcpIsoClientTlsStartupTests
{
    // ── Production + non-loopback host + TLS disabled → fail-fast ───────────

    [Fact]
    public void Production_NonLoopbackHost_TlsDisabled_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("IsoClient:Host", "acquirer.example.com");
                b.UseSetting("IsoClient:UseTls", "false");
            });

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("IsoClient:UseTls", ex.Message);
        Assert.Contains("acquirer.example.com", ex.Message);
    }

    // ── Production + loopback host + TLS disabled → permitted ───────────────

    [Fact]
    public void Production_LoopbackHost_TlsDisabled_StartsSuccessfully()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("IsoClient:Host", "127.0.0.1");
                b.UseSetting("IsoClient:UseTls", "false");
            });

        var client = factory.CreateClient(); // must NOT throw
        Assert.NotNull(client);
    }

    // ── Development + non-loopback host + TLS disabled → permitted ──────────

    [Fact]
    public void Development_NonLoopbackHost_TlsDisabled_StartsSuccessfully()
    {
        // IsoSwitchWebApplicationFactory defaults to the Development environment.
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("IsoClient:Host", "acquirer.example.com");
                b.UseSetting("IsoClient:UseTls", "false");
            });

        var client = factory.CreateClient();
        Assert.NotNull(client);
    }

    // ── Production + unresolvable host + TLS disabled → fail closed ─────────

    [Fact]
    public void Production_UnresolvableHost_TlsDisabled_ThrowsOnStart()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                // RFC 2606 reserved TLD — guaranteed never to resolve, and never
                // treated as loopback, so this proves the fail-closed DNS path.
                b.UseSetting("IsoClient:Host", "this-host-does-not-exist.invalid");
                b.UseSetting("IsoClient:UseTls", "false");
            });

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("this-host-does-not-exist.invalid", ex.Message);
    }

    // ── Production + non-loopback host + TLS enabled (new default) → OK ─────

    [Fact]
    public void Production_NonLoopbackHost_TlsEnabled_StartsSuccessfully()
    {
        using var factory = new IsoSwitchWebApplicationFactory()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("IsoClient:Host", "acquirer.example.com");
                b.UseSetting("IsoClient:UseTls", "true");
            });

        var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}
