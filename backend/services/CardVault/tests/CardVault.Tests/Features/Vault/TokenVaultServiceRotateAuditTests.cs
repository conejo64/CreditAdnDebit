using BuildingBlocks.Outbox;
using CardVault.Api.Pci;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Vault;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using System.Text.Json;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// TDD unit tests (RED before T-07 implementation): TokenVaultService.RotateActiveKeyAsync
/// must write exactly one OutboxMessageEntity with the correct audit payload and must NOT
/// fire-and-forget via IEventBus for the rotation audit.
/// </summary>
public sealed class TokenVaultServiceRotateAuditTests
{
    private static TokenVaultService BuildService(CardVaultDbContext db)
    {
        var vaultOpt = new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        };
        var crypto   = new VaultCrypto(vaultOpt);
        var bus      = new NullEventBus();
        var settings = new VaultSettingsStore(db);
        var pciOpt   = new PciOptions();
        var pciAudit = new PciAuditPublisher(bus);

        return new TokenVaultService(db, crypto, bus, settings, pciOpt, pciAudit);
    }

    // ─── Scenario: rotate writes exactly one outbox row ───────────────────────

    [Fact]
    public async Task RotateActiveKeyAsync_WritesExactlyOneOutboxRow()
    {
        // Arrange
        var db  = TestDbContextFactory.Create();
        var svc = BuildService(db);
        var ct  = CancellationToken.None;

        // Act
        await svc.RotateActiveKeyAsync("k2", "test-actor", "trace-001", ct);

        // Assert — exactly one outbox row
        var rows = db.OutboxMessages.ToList();
        rows.Should().HaveCount(1, because: "rotation must produce exactly one audit row");
    }

    [Fact]
    public async Task RotateActiveKeyAsync_OutboxRow_HasCorrectTopic()
    {
        var db  = TestDbContextFactory.Create();
        var svc = BuildService(db);

        await svc.RotateActiveKeyAsync("k2", "test-actor", "trace-001", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        row.Topic.Should().Be("sw.cardvault.audit",
            because: "vault audit events go to the sw.cardvault.audit topic");
    }

    [Fact]
    public async Task RotateActiveKeyAsync_OutboxRow_PayloadHasCorrectFields()
    {
        var db  = TestDbContextFactory.Create();
        var svc = BuildService(db);

        await svc.RotateActiveKeyAsync("k2", "test-actor", "trace-001", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        var payload = JsonDocument.Parse(row.PayloadJson).RootElement;

        payload.GetProperty("type").GetString().Should().Be("cardvault.vault.rotate");
        payload.GetProperty("actor").GetString().Should().Be("test-actor");
        payload.GetProperty("traceId").GetString().Should().Be("trace-001");

        // keyId must be present and non-empty
        payload.TryGetProperty("keyId", out var keyIdProp).Should().BeTrue();
        keyIdProp.GetString().Should().NotBeNullOrEmpty();

        // rotatedAt must be present and parseable as UTC
        payload.TryGetProperty("rotatedAt", out var rotatedAtProp).Should().BeTrue();
        DateTimeOffset.TryParse(rotatedAtProp.GetString(), out var rotatedAt).Should().BeTrue();
        rotatedAt.Offset.Should().Be(TimeSpan.Zero, because: "audit timestamps must be UTC");
    }

    [Fact]
    public async Task RotateActiveKeyAsync_OutboxRow_PayloadContainsNoKeyMaterial()
    {
        var db  = TestDbContextFactory.Create();
        var svc = BuildService(db);

        await svc.RotateActiveKeyAsync("k2", "test-actor", "trace-001", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        var json = row.PayloadJson.ToLowerInvariant();

        // Base64 key material patterns — 32 bytes = 44 chars base64, check for known key values
        json.Should().NotContain("g64ak3q44+yrgd5mjgkq2d/4tdedomdrzjoihsa11q=".ToLowerInvariant(),
            because: "key material must never appear in the audit payload");
        json.Should().NotContain("4cnkcnxou7qpb4jevuw/fbmrdw9azcxik/tkkoj/goay=".ToLowerInvariant(),
            because: "key material must never appear in the audit payload");
        // Common key-material field names must not appear
        json.Should().NotContain("\"nonceb64\"");
        json.Should().NotContain("\"cipherb64\"");
        json.Should().NotContain("\"tagb64\"");
    }

    // ─── Scenario: rotate then reencrypt → 2 outbox rows ─────────────────────

    [Fact]
    public async Task RotateThenReencrypt_ProducesTwoSeparateOutboxRows()
    {
        // Arrange — fresh DB; seed 2 entries under k1, then rotate to k2, then reencrypt
        var dbName = $"Vault_Test_{Guid.NewGuid():N}";
        var db     = TestDbContextFactory.Create(dbName);

        // Seed VaultSettings with k1 as active so rotation to k2 is valid
        db.VaultSettings.Add(new VaultSettingsEntity
        {
            Id          = Guid.NewGuid(),
            ActiveKeyId = "k1",
            UpdatedOn   = DateTimeOffset.UtcNow
        });
        // Seed 2 token vault entries under k1 so re-encrypt has work to do
        var k1Crypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        });
        for (int i = 0; i < 2; i++)
        {
            var parts = k1Crypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload($"411111111111{i:D4}", null));
            db.TokenVault.Add(new TokenVaultEntryEntity
            {
                Id            = Guid.NewGuid(),
                Token         = $"tok_seed_{i}",
                KeyId         = "k1",
                NonceB64      = parts.nonceB64,
                CiphertextB64 = parts.cipherB64,
                TagB64        = parts.tagB64,
                CreatedOn     = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var svc = BuildService(db);
        var ct  = CancellationToken.None;

        // Act
        await svc.RotateActiveKeyAsync("k2", "test-actor", "trace-001", ct);
        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace", ct);

        // Assert — exactly 2 audit rows, one per operation
        var rows = db.OutboxMessages.ToList();
        rows.Should().HaveCount(2,
            because: "rotate and reencrypt each produce their own audit row");

        rows.Should().Contain(r => GetType(r.PayloadJson) == "cardvault.vault.rotate",
            because: "one row must be the rotate audit");

        rows.Should().Contain(r => GetType(r.PayloadJson) == "cardvault.reencrypt.batch",
            because: "one row must be the reencrypt batch audit");
    }

    // ─── Minimal no-op IEventBus ──────────────────────────────────────────────
    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
            => Task.CompletedTask;
    }

    private static string? GetType(string payloadJson)
    {
        var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : null;
    }
}
