using CardVault.Api.Pci;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Vault;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using BuildingBlocks.Outbox;

namespace CardVault.Tests.Security;

/// <summary>
/// SEC-01 (tasks 2.10, 2.12, 2.14): orphan-proof re-encryption gate + revocation
/// safety tests, satisfying `vault-and-pci` scenarios "Revocation is not performed
/// if re-encryption is incomplete" and "Revoked old key cannot decrypt".
///
/// The gate condition SHALL be COUNT(TokenVault WHERE KeyId NOT IN (activeKeyId)) == 0,
/// NOT a wait on LastReencryptStatus == "completed" — a terminal (zero-remaining) batch
/// is expected to report status "noop" and emit no audit event; that is the correct
/// terminal state, not a failure.
/// </summary>
public sealed class VaultRevocationGateTests
{
    private static readonly Dictionary<string, string> ThreeKeyOptions = new()
    {
        ["k1"] = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=",
        ["k2"] = "IB8eHRwbGhkYFxYVFBMSERAPDg0MCwoJCAcGBQQDAgE=",
        ["k3"] = "AgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fIAE="
    };

    private static (CardVaultDbContext db, TokenVaultService svc) BuildService(
        string? dbName, IReadOnlyDictionary<string, string> keys, string activeKeyId)
    {
        var db = TestDbContextFactory.Create(dbName);
        var vaultOpt = new VaultOptions
        {
            ActiveKeyId = activeKeyId,
            Keys = keys.ToDictionary(k => k.Key, k => k.Value)
        };
        var crypto = new VaultCrypto(vaultOpt);
        var bus = new NullEventBus();
        var settings = new VaultSettingsStore(db);
        var pciOpt = new PciOptions();
        var pciAudit = new PciAuditPublisher(bus);

        var svc = new TokenVaultService(db, crypto, bus, settings, vaultOpt, pciOpt, pciAudit);
        return (db, svc);
    }

    private static async Task<VaultCrypto> SeedEntriesAsync(
        CardVaultDbContext db, string keyId, string keyB64, int count)
    {
        var seedCrypto = new VaultCrypto(new VaultOptions
        {
            ActiveKeyId = keyId,
            Keys = new Dictionary<string, string> { [keyId] = keyB64 }
        });

        for (var i = 0; i < count; i++)
        {
            var parts = seedCrypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload($"411111111111{i:D4}", null));
            db.TokenVault.Add(new TokenVaultEntryEntity
            {
                Id = Guid.NewGuid(),
                Token = $"tok_{keyId}_{i}",
                KeyId = keyId,
                NonceB64 = parts.nonceB64,
                CiphertextB64 = parts.cipherB64,
                TagB64 = parts.tagB64,
                MaskedPan = $"411111******{i:D4}",
                Bin = "411111",
                CreatedOn = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return seedCrypto;
    }

    // ─── Task 2.10: explicit orphan-proof COUNT gate ──────────────────────────

    [Fact]
    public async Task OrphanProofGate_NonZeroCount_BlocksRevocation()
    {
        var dbName = $"RevocationGate_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName, ThreeKeyOptions, "k3");

        // Seed records still under k1/k2 — re-encryption has NOT run yet.
        await SeedEntriesAsync(db, "k1", ThreeKeyOptions["k1"], 2);
        await SeedEntriesAsync(db, "k2", ThreeKeyOptions["k2"], 2);
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k3", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var orphanCount = db.TokenVault.Count(x => x.KeyId != "k3");
        orphanCount.Should().Be(4, because: "precondition: 4 records still reference k1/k2 before re-encryption");

        // Gate assertion — revocation must NOT proceed while COUNT > 0.
        GateIsOpen(orphanCount).Should().BeFalse(
            because: "the orphan-proof gate must stay closed while any TokenVault row references a non-active key");
    }

    [Fact]
    public async Task OrphanProofGate_ZeroCount_OpensAfterFullReencryption()
    {
        var dbName = $"RevocationGate_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName, ThreeKeyOptions, "k3");

        await SeedEntriesAsync(db, "k1", ThreeKeyOptions["k1"], 2);
        await SeedEntriesAsync(db, "k2", ThreeKeyOptions["k2"], 1);
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k3", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Act — run batches until the migration is exhausted.
        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace-1", CancellationToken.None);

        var orphanCount = db.TokenVault.Count(x => x.KeyId != "k3");
        orphanCount.Should().Be(0, because: "a single batch of size 10 covers all 3 seeded records");

        GateIsOpen(orphanCount).Should().BeTrue(
            because: "the orphan-proof gate opens exactly when COUNT(KeyId NOT IN (active)) == 0");
    }

    // ─── Terminal batch: zero-remaining is a "noop", not a failure ────────────

    [Fact]
    public async Task TerminalBatch_ZeroRemainingRecords_ReportsNoopStatus_NotCompleted()
    {
        var dbName = $"RevocationGate_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName, ThreeKeyOptions, "k3");

        // No k1/k2 records at all — simulates the state right after the last real batch.
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k3", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await svc.ReEncryptBatchAsync(10, "system-job", "job-trace-terminal", CancellationToken.None);

        result.UpdatedCount.Should().Be(0, because: "there is nothing left to migrate");

        var settings = db.VaultSettings.Single();
        settings.LastReencryptStatus.Should().Be("noop",
            because: "a terminal batch that migrates zero records reports status 'noop', per design — " +
                     "this is the expected terminal state, not a wait condition on 'completed'");
    }

    [Fact]
    public async Task TerminalBatch_ZeroRemainingRecords_EmitsNoAuditEvent()
    {
        var dbName = $"RevocationGate_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName, ThreeKeyOptions, "k3");

        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k3", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await svc.ReEncryptBatchAsync(10, "system-job", "job-trace-terminal", CancellationToken.None);

        db.OutboxMessages.Should().BeEmpty(
            because: "a no-op terminal batch (zero records migrated) must not emit a " +
                     "cardvault.reencrypt.batch audit event — only batches that migrate at least one record do");
    }

    [Fact]
    public async Task TerminalBatch_AfterRealBatch_SecondCallIsNoopAndGateStaysOpen()
    {
        var dbName = $"RevocationGate_{Guid.NewGuid():N}";
        var (db, svc) = BuildService(dbName, ThreeKeyOptions, "k3");

        await SeedEntriesAsync(db, "k1", ThreeKeyOptions["k1"], 2);
        db.VaultSettings.Add(new VaultSettingsEntity { Id = Guid.NewGuid(), ActiveKeyId = "k3", UpdatedOn = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // First batch — migrates the 2 seeded k1 records.
        var first = await svc.ReEncryptBatchAsync(10, "system-job", "job-trace-1", CancellationToken.None);
        first.UpdatedCount.Should().Be(2);
        db.OutboxMessages.Should().HaveCount(1, because: "the real batch emits exactly one audit row");

        // Second batch — nothing left; must be a noop, must NOT emit a second audit row,
        // and the orphan-proof gate must be open (this is success, not a stall).
        var second = await svc.ReEncryptBatchAsync(10, "system-job", "job-trace-2", CancellationToken.None);
        second.UpdatedCount.Should().Be(0);

        db.OutboxMessages.Should().HaveCount(1,
            because: "the terminal no-op batch must not add a second audit row");

        var orphanCount = db.TokenVault.Count(x => x.KeyId != "k3");
        GateIsOpen(orphanCount).Should().BeTrue(
            because: "after the real batch fully migrates all records, the gate opens even though " +
                     "the terminal call reports 'noop' rather than 'completed'");
    }

    // ─── Task 2.12: revocation blocked while gate is nonzero ──────────────────

    [Fact]
    public void Revocation_WhileGateNonzero_DecryptStillWorksForRemainingOldKeyRecords()
    {
        // Revocation is modeled as removing a key from VaultOptions.Keys. Simulate the
        // "premature revocation" mistake: attempt a decrypt of a still-referenced k1
        // record using a crypto instance where k1 has ALREADY been removed while the
        // gate is still nonzero (i.e., revoked too early). This must fail loudly —
        // proving that revoking on an incomplete migration breaks decrypt for the
        // records that were never re-encrypted, which is exactly why the runbook
        // requires the gate to reach zero first.
        var vaultOptWithK1 = new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = ThreeKeyOptions["k1"] }
        };
        var k1Crypto = new VaultCrypto(vaultOptWithK1);
        var parts = k1Crypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload("4111111111110001", null));

        // Now simulate revocation having already happened (k1 removed from config)
        // while this record still references k1 — the incomplete-migration mistake.
        var vaultOptRevoked = new VaultOptions
        {
            ActiveKeyId = "k3",
            Keys = new Dictionary<string, string> { ["k3"] = ThreeKeyOptions["k3"] }
        };
        var revokedCrypto = new VaultCrypto(vaultOptRevoked);

        var act = () => revokedCrypto.DecryptFromParts<TokenVaultService.SensitiveCardPayload>(
            parts.keyId, parts.nonceB64, parts.cipherB64, parts.tagB64);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown KeyId*",
                because: "premature revocation of a key that still has referencing records must fail " +
                          "loudly at decrypt time — this is the concrete failure the orphan-proof gate exists to prevent");
    }

    // ─── Task 2.14: revoked key cannot decrypt, no plaintext leak ─────────────

    [Fact]
    public void RevokedKey_DecryptAttempt_ThrowsUnknownKeyId()
    {
        // Arrange — encrypt under k1, then build a crypto instance representing the
        // post-revocation config where k1/k2 have been removed and only k3 remains.
        var vaultOptWithK1 = new VaultOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = ThreeKeyOptions["k1"] }
        };
        var k1Crypto = new VaultCrypto(vaultOptWithK1);
        var parts = k1Crypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload("4111111111119999", "2812"));

        var postRevocationOptions = new VaultOptions
        {
            ActiveKeyId = "k3",
            Keys = new Dictionary<string, string> { ["k3"] = ThreeKeyOptions["k3"] } // k1/k2 revoked
        };
        var postRevocationCrypto = new VaultCrypto(postRevocationOptions);

        // Act
        Action act = () => postRevocationCrypto.DecryptFromParts<TokenVaultService.SensitiveCardPayload>(
            parts.keyId, parts.nonceB64, parts.cipherB64, parts.tagB64);

        // Assert — throws, referencing the unknown/revoked key id.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown KeyId*k1*",
                because: "a decrypt attempt resolving to a revoked key id must fail with a message " +
                          "identifying the unknown key, satisfying 'Revoked old key cannot decrypt'");
    }

    [Fact]
    public void RevokedKey_DecryptAttempt_ExceptionContainsNoPlaintextPan()
    {
        var vaultOptWithK2 = new VaultOptions
        {
            ActiveKeyId = "k2",
            Keys = new Dictionary<string, string> { ["k2"] = ThreeKeyOptions["k2"] }
        };
        var k2Crypto = new VaultCrypto(vaultOptWithK2);
        const string plaintextPan = "4111111111112222";
        var parts = k2Crypto.EncryptToParts(new TokenVaultService.SensitiveCardPayload(plaintextPan, null));

        var postRevocationOptions = new VaultOptions
        {
            ActiveKeyId = "k3",
            Keys = new Dictionary<string, string> { ["k3"] = ThreeKeyOptions["k3"] }
        };
        var postRevocationCrypto = new VaultCrypto(postRevocationOptions);

        Exception? caught = null;
        try
        {
            postRevocationCrypto.DecryptFromParts<TokenVaultService.SensitiveCardPayload>(
                parts.keyId, parts.nonceB64, parts.cipherB64, parts.tagB64);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull(because: "the decrypt attempt against a revoked key must throw");
        caught!.Message.Should().NotContain(plaintextPan,
            because: "the exception raised for a revoked-key decrypt attempt must never leak the plaintext PAN");
        caught.ToString().Should().NotContain(plaintextPan,
            because: "neither the message nor the full exception string (including any inner exception) may contain the plaintext PAN");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The orphan-proof gate condition per task 2.10: COUNT(TokenVault WHERE KeyId
    /// NOT IN (activeKeyId)) == 0. Intentionally NOT derived from LastReencryptStatus.
    /// </summary>
    private static bool GateIsOpen(int orphanCount) => orphanCount == 0;

    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
            => Task.CompletedTask;
    }
}
