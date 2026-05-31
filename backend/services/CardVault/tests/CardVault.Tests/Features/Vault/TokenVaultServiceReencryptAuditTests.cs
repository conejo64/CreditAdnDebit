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
/// TDD unit tests (RED before T-09 implementation): TokenVaultService.ReEncryptBatchAsync
/// must write exactly one OutboxMessageEntity with the correct audit payload.
/// Actor "system-job" confirms the scheduler-triggered path also emits audit (resolved decision #1).
/// </summary>
public sealed class TokenVaultServiceReencryptAuditTests
{
    private static (CardVaultDbContext db, TokenVaultService svc) BuildService(string? dbName = null)
    {
        var db = TestDbContextFactory.Create(dbName);
        var vaultOpt = new VaultOptions
        {
            ActiveKeyId = "k2",
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

        var svc = new TokenVaultService(db, crypto, bus, settings, pciOpt, pciAudit);
        return (db, svc);
    }

    private static async Task SeedTokenVaultEntriesAsync(
        CardVaultDbContext db, VaultCrypto crypto, int count, string keyId)
    {
        for (int i = 0; i < count; i++)
        {
            var parts = crypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload($"411111111111{i:D4}", null));
            db.TokenVault.Add(new TokenVaultEntryEntity
            {
                Id           = Guid.NewGuid(),
                Token        = $"tok_seed_{i}",
                KeyId        = keyId,
                NonceB64     = parts.nonceB64,
                CiphertextB64 = parts.cipherB64,
                TagB64       = parts.tagB64,
                MaskedPan    = $"411111******{i:D4}",
                Bin          = "411111",
                CreatedOn    = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    // ─── Scenario: reencrypt writes exactly one outbox row ────────────────────

    [Fact]
    public async Task ReEncryptBatchAsync_WithRecordsToMigrate_WritesExactlyOneOutboxRow()
    {
        // Arrange — 3 entries encrypted with k1; active key is k2
        var dbName = $"ReencryptAudit_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName);

        // Temporarily use k1 crypto to encrypt the seed entries
        var k1Crypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        });
        await SeedTokenVaultEntriesAsync(db, k1Crypto, 3, "k1");

        // Seed VaultSettings with active key k2
        db.VaultSettings.Add(new VaultSettingsEntity
        {
            Id          = Guid.NewGuid(),
            ActiveKeyId = "k2",
            UpdatedOn   = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace", CancellationToken.None);

        // Assert — exactly one outbox row (plus any from seeding, but seeding doesn't add outbox rows)
        var rows = db.OutboxMessages.ToList();
        rows.Should().HaveCount(1, because: "reencrypt must produce exactly one audit row");
    }

    [Fact]
    public async Task ReEncryptBatchAsync_OutboxRow_HasCorrectTopic()
    {
        var dbName = $"ReencryptAudit_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName);
        var k1Crypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        });
        await SeedTokenVaultEntriesAsync(db, k1Crypto, 3, "k1");
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k2", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        row.Topic.Should().Be("sw.cardvault.audit");
    }

    [Fact]
    public async Task ReEncryptBatchAsync_OutboxRow_PayloadHasCorrectFields()
    {
        var dbName = $"ReencryptAudit_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName);
        var k1Crypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        });
        await SeedTokenVaultEntriesAsync(db, k1Crypto, 3, "k1");
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k2", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        var payload = JsonDocument.Parse(row.PayloadJson).RootElement;

        payload.GetProperty("type").GetString().Should().Be("cardvault.reencrypt.batch");
        payload.GetProperty("actor").GetString().Should().Be("system-job",
            because: "scheduler-triggered re-encryption uses actor 'system-job'");
        payload.GetProperty("traceId").GetString().Should().Be("job-trace");

        // recordsAffected must equal the number of migrated entries
        payload.TryGetProperty("recordsAffected", out var affectedProp).Should().BeTrue();
        affectedProp.GetInt32().Should().Be(3);

        // completedAt must be present and UTC
        payload.TryGetProperty("completedAt", out var completedAtProp).Should().BeTrue();
        DateTimeOffset.TryParse(completedAtProp.GetString(), out var completedAt).Should().BeTrue();
        completedAt.Offset.Should().Be(TimeSpan.Zero, because: "audit timestamps must be UTC");
    }

    [Fact]
    public async Task ReEncryptBatchAsync_OutboxRow_PayloadContainsNoKeyMaterial()
    {
        var dbName = $"ReencryptAudit_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName);
        var k1Crypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string>
            {
                ["k1"] = "G64aK3Q44+yrGd5Mjgkq2D/4TDedO3dRwzjIOHsa11Q=",
                ["k2"] = "4cnKCNXOU7qpb4JEVUW/FBmRdW9azcXIK/tkOj/gOaY="
            }
        });
        await SeedTokenVaultEntriesAsync(db, k1Crypto, 3, "k1");
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k2", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace", CancellationToken.None);

        var row = db.OutboxMessages.Single();
        var json = row.PayloadJson.ToLowerInvariant();

        json.Should().NotContain("g64ak3q44+yrgd5mjgkq2d/4tdedomdrzjoihsa11q=".ToLowerInvariant());
        json.Should().NotContain("4cnkcnxou7qpb4jevuw/fbmrdw9azcxik/tkkoj/goay=".ToLowerInvariant());
        json.Should().NotContain("\"nonceb64\"");
        json.Should().NotContain("\"cipherb64\"");
        json.Should().NotContain("\"tagb64\"");
        // PAN-like strings (16 digit sequences) must not appear
        json.Should().NotMatchRegex(@"\b4111[0-9]{12}\b",
            because: "plaintext PANs must never appear in audit payload");
    }

    // ─── Minimal no-op IEventBus ──────────────────────────────────────────────
    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
            => Task.CompletedTask;
    }
}
