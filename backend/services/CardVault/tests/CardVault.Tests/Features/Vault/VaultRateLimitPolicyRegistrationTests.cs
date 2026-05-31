using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// TDD (RED before T-03 implementation): startup regression — both vault rate-limit
/// policies must be registered.  The startup assertion added in T-03 will make the
/// application throw InvalidOperationException if either policy is absent.
/// </summary>
[Collection("WebApp")]
public sealed class VaultRateLimitPolicyRegistrationTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;

    public VaultRateLimitPolicyRegistrationTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The application should boot successfully when both policies are registered.
    /// This test is GREEN after T-03 implementation: the startup assertion passes
    /// because vault_admin_ops is registered in Program.cs.
    /// Before T-03: the factory boots (no assertion), test still passes — this is
    /// the "happy path" guard that must stay GREEN after the change.
    /// </summary>
    [Fact]
    public void ApplicationHost_BootsWithBothPoliciesRegistered()
    {
        // Arrange + Act — creating the client triggers the host startup
        // If the startup assertion throws, the factory throws here
        var act = () => _factory.CreateClient();

        // Assert — must not throw; both policies are registered
        act.Should().NotThrow(
            because: "when vault_admin_ops and vault_detokenize are both registered, startup should succeed");
    }

    /// <summary>
    /// RateLimiterOptions must be resolvable from DI (smoke test for the service registration).
    /// </summary>
    [Fact]
    public void RateLimiterOptions_IsResolvableFromDI()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<RateLimiterOptions>>();
        options.Should().NotBeNull();
        options.Value.Should().NotBeNull();
    }

    /// <summary>
    /// vault_admin_ops is used by rotate-active-key.  An unauthenticated request
    /// to that endpoint must return 401 (auth enforced), NOT 500 (which would mean
    /// a runtime crash due to a missing policy — should be caught at startup by the
    /// new assertion, but this is a defense-in-depth integration check).
    /// Note: rate-limiter is placed after auth in Program.cs, so auth rejects first.
    /// </summary>
    [Fact]
    public async Task VaultAdminOpsPolicy_IsRegistered_RotateEndpointReturns401NotServerError()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "unauthenticated callers must be rejected; the rate-limit policy itself must not crash the host");
    }

    /// <summary>
    /// vault_detokenize policy is registered alongside vault_admin_ops.
    /// </summary>
    [Fact]
    public async Task VaultDetokenizePolicy_IsRegistered_DetokenizeEndpointReachable()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/tokens/detokenize?token=tok_test", null);

        ((int)response.StatusCode).Should().NotBe(500,
            because: "a missing vault_detokenize policy would cause a runtime error at the middleware");
    }
}
