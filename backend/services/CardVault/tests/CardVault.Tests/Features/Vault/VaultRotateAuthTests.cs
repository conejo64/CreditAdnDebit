using CardVault.Infrastructure.Persistence;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// TDD auth-boundary tests for vault rotation endpoints.
/// Verifies that unauthorized callers are rejected with 403/401
/// and that no audit rows are emitted on rejected requests.
/// </summary>
[Collection("WebApp")]
public sealed class VaultRotateAuthTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;

    public VaultRotateAuthTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── Scenario A: Auditor (no CanRotateVaultKeys) → 403 on rotate ──────────

    [Fact]
    public async Task RotateActiveKey_AuditorRole_Returns403()
    {
        // Arrange — Auditor does NOT satisfy CanRotateVaultKeys (Admin only)
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "Auditor role does not have CanRotateVaultKeys permission");
    }

    // ─── Scenario B: Auditor → 403 on reencrypt ───────────────────────────────

    [Fact]
    public async Task Reencrypt_AuditorRole_Returns403()
    {
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/vault/reencrypt?take=10", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "Auditor role does not have CanRotateVaultKeys permission");
    }

    // ─── Scenario C: No JWT → 401 ─────────────────────────────────────────────

    [Fact]
    public async Task RotateActiveKey_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();
        // No Authorization header
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "missing authentication must return 401");
    }

    [Fact]
    public async Task Reencrypt_NoJwt_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/vault/reencrypt?take=10", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "missing authentication must return 401");
    }

    // ─── Verify no outbox row on 403 ──────────────────────────────────────────

    [Fact]
    public async Task RotateActiveKey_ForbiddenRequest_AddsNoOutboxRow()
    {
        // Arrange — Auditor role → 403
        var token = _factory.GenerateJwt(roles: ["Auditor"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scopeBefore = _factory.Services.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countBefore = dbBefore.OutboxMessages.Count();

        // Act
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Assert — no new outbox rows
        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countAfter = dbAfter.OutboxMessages.Count();

        countAfter.Should().Be(countBefore,
            because: "a 403 Forbidden response must not produce any audit outbox row");
    }
}
