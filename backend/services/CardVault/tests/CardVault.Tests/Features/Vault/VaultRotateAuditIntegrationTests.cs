using CardVault.Infrastructure.Persistence;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// End-to-end integration tests verifying that the HTTP rotation/reencrypt endpoints
/// produce outbox audit rows with correct payloads (T-10, T-11, T-12).
/// These are GREEN after WU3 implementation (transactional outbox audit).
/// </summary>
[Collection("WebApp")]
public sealed class VaultRotateAuditIntegrationTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;

    public VaultRotateAuditIntegrationTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── T-10: Successful rotate emits one outbox row with correct payload ─────

    [Fact]
    public async Task RotateActiveKey_Success_EmitsExactlyOneOutboxAuditRow()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countBefore = db.OutboxMessages.Count(m => m.Topic == "sw.cardvault.audit");

        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Assert — HTTP 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — exactly one new audit row
        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var auditRows = dbAfter.OutboxMessages
            .Where(m => m.Topic == "sw.cardvault.audit")
            .ToList();

        auditRows.Count.Should().Be(countBefore + 1,
            because: "a successful rotation must produce exactly one audit outbox row");
    }

    [Fact]
    public async Task RotateActiveKey_Success_AuditRowPayloadHasCorrectFields()
    {
        // Arrange
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert payload fields
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var row = db.OutboxMessages
            .Where(m => m.Topic == "sw.cardvault.audit")
            .OrderByDescending(m => m.OccurredOn)
            .First();

        var payload = JsonDocument.Parse(row.PayloadJson).RootElement;

        payload.GetProperty("type").GetString().Should().Be("cardvault.vault.rotate");
        payload.TryGetProperty("actor", out var actorProp).Should().BeTrue();
        actorProp.GetString().Should().NotBeNullOrEmpty(because: "actor must be the authenticated user identity");
        payload.TryGetProperty("keyId", out var keyIdProp).Should().BeTrue();
        keyIdProp.GetString().Should().NotBeNullOrEmpty();
        payload.TryGetProperty("rotatedAt", out var rotatedAtProp).Should().BeTrue();
        DateTimeOffset.TryParse(rotatedAtProp.GetString(), out var rotatedAt).Should().BeTrue();
        rotatedAt.Offset.Should().Be(TimeSpan.Zero, because: "timestamps must be UTC");
    }

    [Fact]
    public async Task RotateActiveKey_Success_AuditPayloadContainsNoKeyMaterial()
    {
        // Arrange
        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);

        // Assert — no key bytes in payload
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var row = db.OutboxMessages
            .Where(m => m.Topic == "sw.cardvault.audit")
            .OrderByDescending(m => m.OccurredOn)
            .First();

        var json = row.PayloadJson;
        json.Should().NotContain("nonceB64",  because: "key material must not appear in audit payload");
        json.Should().NotContain("cipherB64", because: "key material must not appear in audit payload");
        json.Should().NotContain("tagB64",    because: "key material must not appear in audit payload");
        // Known key values must not appear
        json.Should().NotContain("G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=");
        json.Should().NotContain("4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY=");
    }

    // ─── T-11: Successful reencrypt emits one outbox row ──────────────────────

    [Fact]
    public async Task Reencrypt_Success_EmitsOutboxAuditRowWithCorrectType()
    {
        // Arrange
        using var scopeBefore = _factory.Services.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countBefore = dbBefore.OutboxMessages.Count(m => m.Topic == "sw.cardvault.audit");

        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsync("/api/vault/reencrypt?take=10", null);

        // Assert HTTP 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert outbox row exists (if there were records to migrate)
        // If 0 records are migrated, no audit row is expected (spec: emit only on completion)
        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<CardVaultDbContext>();

        var reencryptRows = dbAfter.OutboxMessages
            .Where(m => m.Topic == "sw.cardvault.audit")
            .ToList()
            .Where(m =>
            {
                if (!m.PayloadJson.Contains("cardvault.reencrypt.batch")) return false;
                var doc = JsonDocument.Parse(m.PayloadJson);
                return doc.RootElement.GetProperty("type").GetString() == "cardvault.reencrypt.batch";
            })
            .ToList();

        // Response payload must have recordsAffected (integer) and completedAt
        var responseBody = await response.Content.ReadAsStringAsync();
        var responseDoc = JsonDocument.Parse(responseBody).RootElement;
        responseDoc.TryGetProperty("updatedCount", out var updatedCount).Should().BeTrue();

        if (updatedCount.GetInt32() > 0)
        {
            reencryptRows.Should().HaveCount(1,
                because: "a completed reencrypt batch with records must emit exactly one audit row");

            var payload = JsonDocument.Parse(reencryptRows[0].PayloadJson).RootElement;
            payload.GetProperty("recordsAffected").GetInt32().Should().Be(updatedCount.GetInt32());
            payload.TryGetProperty("completedAt", out _).Should().BeTrue();
        }
    }

    // ─── T-12: Throttled request produces no new audit row ────────────────────

    [Fact]
    public async Task RotateActiveKey_ThrottledByRateLimit_ProducesNoAdditionalAuditRow()
    {
        // Arrange — PermitLimit=1 so second request gets 429
        using var throttledFactory = _factory.WithWebHostBuilder(hostBuilder =>
        {
            hostBuilder.UseSetting("Vault:AdminRateLimit:PermitLimit", "1");
            hostBuilder.UseSetting("Vault:AdminRateLimit:WindowSeconds", "30");
            hostBuilder.UseSetting("Vault:AdminRateLimit:QueueLimit", "0");
        });

        var token = _factory.GenerateJwt(roles: ["Admin"]);
        var client = throttledFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // First request — expected to succeed
        var first = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, because: "first request within limit");

        // Count rows after first request
        using var scopeAfterFirst = throttledFactory.Services.CreateScope();
        var dbAfterFirst = scopeAfterFirst.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countAfterFirst = dbAfterFirst.OutboxMessages.Count(m => m.Topic == "sw.cardvault.audit");

        // Second request — expected to be throttled
        var second = await client.PostAsync("/api/vault/rotate-active-key?keyId=k1", null);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, because: "second request exceeds limit");

        // Assert — outbox row count must be unchanged after throttled request
        using var scopeAfterSecond = throttledFactory.Services.CreateScope();
        var dbAfterSecond = scopeAfterSecond.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var countAfterSecond = dbAfterSecond.OutboxMessages.Count(m => m.Topic == "sw.cardvault.audit");

        countAfterSecond.Should().Be(countAfterFirst,
            because: "a 429 throttled request must not produce any audit outbox row");
    }
}
