using CardVault.Infrastructure.Persistence;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// TDD integration tests for vault_admin_ops rate-limiting on rotate and reencrypt.
/// These tests must be GREEN after T-03 implementation (vault_admin_ops registered).
/// </summary>
public sealed class VaultRotateRateLimitTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;

    public VaultRotateRateLimitTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── Scenario A: 200 authorized rotate within permit limit ─────────────────

    [Fact]
    public async Task RotateActiveKey_AuthorizedWithinWindow_Returns200()
    {
        // Arrange — default dev PermitLimit (20), first request always allowed
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Assert — key stays k1→k1, should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "an authorized Admin rotating to the same key within the window must return 200");
    }

    // ─── Scenario B: 429 on burst for rotate ───────────────────────────────────

    [Fact]
    public async Task RotateActiveKey_ExceedsPermitLimit_Returns429()
    {
        // Arrange — use a throttled factory (PermitLimit=1)
        using var throttledFactory = CreateThrottledFactory();
        var token = _factory.GenerateJwt(roles: ["Admin"]); // token format is same across factories
        var client = throttledFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act — first request must succeed, second must be throttled
        var first  = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        var second = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the first request is within the permit limit");
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            because: "the second request exceeds the PermitLimit=1 window");
    }

    // ─── Scenario B: 429 on burst → no new outbox rows ─────────────────────────

    [Fact]
    public async Task RotateActiveKey_ThrottledRequest_AddsNoOutboxRow()
    {
        // Arrange — PermitLimit = 1
        using var throttledFactory = CreateThrottledFactory();
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = throttledFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // First request (allowed)
        await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Count outbox rows after first request
        using var scopeAfterFirst = throttledFactory.Services.CreateScope();
        var dbAfterFirst = scopeAfterFirst.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countAfterFirst = dbAfterFirst.OutboxMessages.Count();

        // Second request (throttled)
        var throttledResponse = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        throttledResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Count outbox rows after throttled request — must be unchanged
        using var scopeAfterThrottled = throttledFactory.Services.CreateScope();
        var dbAfterThrottled = scopeAfterThrottled.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countAfterThrottled = dbAfterThrottled.OutboxMessages.Count();

        countAfterThrottled.Should().Be(countAfterFirst,
            because: "a throttled (429) request must not produce any outbox audit row");
    }

    // ─── Scenario C: 429 on burst for reencrypt ────────────────────────────────

    [Fact]
    public async Task Reencrypt_ExceedsPermitLimit_Returns429()
    {
        // Arrange — PermitLimit = 1
        using var throttledFactory = CreateThrottledFactory();
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = throttledFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first  = await client.PostAsync("/api/vault/reencrypt?take=10", null);
        var second = await client.PostAsync("/api/vault/reencrypt?take=10", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the first reencrypt request is within the permit limit");
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            because: "the second reencrypt request exceeds PermitLimit=1");
    }

    // ─── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a WebApplicationFactory variant with PermitLimit=1 to test 429 throttling.
    /// Uses IWebHostBuilder.UseSetting to override the Vault:AdminRateLimit config so the
    /// rate-limiter builds with limit=1 before the factory starts.
    /// </summary>
    private WebApplicationFactory<Program> CreateThrottledFactory()
        => _factory.WithWebHostBuilder(hostBuilder =>
        {
            hostBuilder.UseSetting("Vault:AdminRateLimit:PermitLimit", "1");
            hostBuilder.UseSetting("Vault:AdminRateLimit:WindowSeconds", "30");
            hostBuilder.UseSetting("Vault:AdminRateLimit:QueueLimit", "0");
        });
}
