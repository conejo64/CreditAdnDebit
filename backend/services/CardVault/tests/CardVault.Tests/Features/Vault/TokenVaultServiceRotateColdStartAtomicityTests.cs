using BuildingBlocks.Outbox;
using CardVault.Api.Pci;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Vault;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CardVault.Tests.Features.Vault;

/// <summary>
/// Verifies W-1 atomicity guarantee: on the cold-start path (no pre-existing VaultSettings row),
/// the outbox audit row and the ActiveKeyId change must be committed in the SAME SaveChangesAsync
/// call — not in the cold-start initialization save that creates the VaultSettings row.
///
/// The cold-start bug: VaultSettingsStore.GetAsync calls SaveChangesAsync to create the
/// singleton VaultSettings row. If the outbox row is added to the change tracker BEFORE
/// GetAsync is called (the old ordering), that first save commits the outbox row while
/// the key is still set to its default ("k1"), breaking atomicity.
///
/// The fix: stage the outbox row AFTER GetAsync resolves/creates the settings entity,
/// so both the key mutation and the outbox row are committed in the SAME SaveChangesAsync.
/// </summary>
public sealed class TokenVaultServiceRotateColdStartAtomicityTests
{
    private static (CardVaultDbContext db, TokenVaultService svc) BuildServiceWithSpy(
        string dbName, SaveChangesInterceptorSpy spy)
    {
        var options = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(spy)
            .Options;
        var db = new CardVaultDbContext(options);
        db.Database.EnsureCreated();

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
        var svc      = new TokenVaultService(db, crypto, bus, settings, pciOpt, pciAudit);

        return (db, svc);
    }

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

    /// <summary>
    /// Cold-start atomicity: the outbox row must NOT be staged in the change tracker
    /// before VaultSettings is resolved. We verify this by checking that the outbox row
    /// was committed atomically with the key change — not in the cold-start initialization save.
    ///
    /// Specifically: the first SaveChangesAsync (cold-start VaultSettings creation) must NOT
    /// include OutboxMessageEntity. The final SaveChangesAsync (key update) MUST include
    /// both a VaultSettingsEntity and an OutboxMessageEntity.
    /// </summary>
    [Fact]
    public async Task RotateActiveKeyAsync_ColdStart_OutboxRowNotCommittedInColdStartSave()
    {
        // Arrange — spy interceptor records entity type names at each SaveChangesAsync
        var dbName = $"ColdStartSpy_{Guid.NewGuid():N}";
        var spy    = new SaveChangesInterceptorSpy();
        var (_, svc) = BuildServiceWithSpy(dbName, spy);

        // Act
        await svc.RotateActiveKeyAsync("k2", "cold-actor", "cold-trace", CancellationToken.None);

        // Assert — at least 2 saves happened on the cold-start path
        spy.SaveCallSnapshots.Should().HaveCountGreaterThanOrEqualTo(2,
            because: "cold-start path: first save creates the VaultSettings row, second save commits the key change + outbox row");

        // The FIRST save (cold-start VaultSettings creation) must NOT include OutboxMessageEntity.
        // On the OLD (buggy) ordering, the outbox row was added before GetAsync fired, so it
        // appeared in this first save — committed while the key was still "k1".
        var firstSave = spy.SaveCallSnapshots[0];
        firstSave.Should().NotContain("OutboxMessageEntity",
            because: "the cold-start VaultSettings creation save must not include the outbox audit row — " +
                     "it must only create the initial VaultSettings singleton");

        // The LAST save must include OutboxMessageEntity (atomically with the key change)
        var lastSave = spy.SaveCallSnapshots[^1];
        lastSave.Should().Contain("OutboxMessageEntity",
            because: "the outbox audit row must be committed in the same SaveChangesAsync as the ActiveKeyId change");
    }

    /// <summary>
    /// Cold-start: no pre-existing VaultSettings row.
    /// After RotateActiveKeyAsync completes, both the updated ActiveKeyId and the
    /// outbox audit row must be present, consistent, and durable.
    /// </summary>
    [Fact]
    public async Task RotateActiveKeyAsync_ColdStart_KeyUpdateAndOutboxRowBothPresent()
    {
        // Arrange — fresh DB with NO VaultSettings row (cold-start)
        var dbName = $"ColdStart_{Guid.NewGuid():N}";
        var db     = TestDbContextFactory.Create(dbName);
        db.VaultSettings.Should().BeEmpty(because: "precondition: cold-start has no VaultSettings row");
        var svc = BuildService(db);

        // Act
        var result = await svc.RotateActiveKeyAsync("k2", "cold-actor", "cold-trace", CancellationToken.None);

        // Assert — the active key was updated
        result.ActiveKeyId.Should().Be("k2", because: "rotation target key must be active after rotate");

        // Assert — VaultSettings row reflects the new key
        var settings = db.VaultSettings.Single();
        settings.ActiveKeyId.Should().Be("k2",
            because: "VaultSettings.ActiveKeyId must be persisted atomically with the outbox row");

        // Assert — exactly one outbox audit row (from rotation; NOT an extra row from cold-start path)
        var outboxRows = db.OutboxMessages.ToList();
        outboxRows.Should().HaveCount(1,
            because: "cold-start rotation must produce exactly one audit row, not two");

        // Assert — outbox row payload is consistent with the key that was set
        var payload = JsonDocument.Parse(outboxRows[0].PayloadJson).RootElement;
        payload.GetProperty("type").GetString().Should().Be("cardvault.vault.rotate");
        payload.GetProperty("actor").GetString().Should().Be("cold-actor");
        payload.GetProperty("traceId").GetString().Should().Be("cold-trace");
        payload.GetProperty("keyId").GetString().Should().Be("k2",
            because: "outbox payload keyId must match the key that was activated, not a stale pre-rotation value");
    }

    /// <summary>
    /// Warm-start: VaultSettings row pre-exists.
    /// Same atomicity guarantee — key update and outbox row in one save.
    /// </summary>
    [Fact]
    public async Task RotateActiveKeyAsync_WarmStart_KeyUpdateAndOutboxRowBothPresent()
    {
        // Arrange — DB with pre-existing VaultSettings (warm-start)
        var dbName = $"WarmStart_{Guid.NewGuid():N}";
        var db     = TestDbContextFactory.Create(dbName);
        db.VaultSettings.Add(new VaultSettingsEntity
        {
            Id          = Guid.NewGuid(),
            ActiveKeyId = "k1",
            UpdatedOn   = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildService(db);

        // Act
        var result = await svc.RotateActiveKeyAsync("k2", "warm-actor", "warm-trace", CancellationToken.None);

        // Assert — active key updated
        result.ActiveKeyId.Should().Be("k2");

        // Assert — VaultSettings updated
        var settings = db.VaultSettings.Single();
        settings.ActiveKeyId.Should().Be("k2");

        // Assert — exactly one outbox row
        var outboxRows = db.OutboxMessages.ToList();
        outboxRows.Should().HaveCount(1,
            because: "warm-start rotation must produce exactly one audit row");

        // Payload consistency
        var payload = JsonDocument.Parse(outboxRows[0].PayloadJson).RootElement;
        payload.GetProperty("type").GetString().Should().Be("cardvault.vault.rotate");
        payload.GetProperty("actor").GetString().Should().Be("warm-actor");
        payload.GetProperty("keyId").GetString().Should().Be("k2");
    }

    /// <summary>
    /// Cold-start: a second context (simulating a separate reader after the operation)
    /// must see both the updated key and the outbox row — confirming they were committed
    /// together and are visible to subsequent readers.
    /// </summary>
    [Fact]
    public async Task RotateActiveKeyAsync_ColdStart_BothChangesVisibleFromFreshContext()
    {
        // Arrange — fresh DB, no VaultSettings
        var dbName = $"ColdStartFresh_{Guid.NewGuid():N}";
        var db     = TestDbContextFactory.Create(dbName);
        var svc    = BuildService(db);

        // Act
        await svc.RotateActiveKeyAsync("k2", "reader-actor", "reader-trace", CancellationToken.None);

        // Assert — read from a fresh (second) context to avoid EF change-tracker artefacts
        var db2 = TestDbContextFactory.CreateSecondContext(dbName);

        var settings = db2.VaultSettings.Single();
        settings.ActiveKeyId.Should().Be("k2",
            because: "key change must be durable (visible from a separate DbContext)");

        var outboxRows = db2.OutboxMessages.ToList();
        outboxRows.Should().HaveCount(1,
            because: "outbox row must be durable (visible from a separate DbContext)");

        var payload = JsonDocument.Parse(outboxRows[0].PayloadJson).RootElement;
        payload.GetProperty("keyId").GetString().Should().Be("k2",
            because: "outbox payload keyId must be the newly activated key, committed atomically");
    }

    // ─── EF Core interceptor spy ──────────────────────────────────────────────
    /// <summary>
    /// Records which entity type names were staged (Added or Modified) at each
    /// SaveChangesAsync call. Used to verify ordering of commits.
    /// </summary>
    private sealed class SaveChangesInterceptorSpy : SaveChangesInterceptor
    {
        public List<List<string>> SaveCallSnapshots { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                var snapshot = eventData.Context.ChangeTracker.Entries()
                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                    .Select(e => e.Entity.GetType().Name)
                    .ToList();
                SaveCallSnapshots.Add(snapshot);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // ─── Minimal no-op IEventBus ──────────────────────────────────────────────
    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
            => Task.CompletedTask;
    }
}
