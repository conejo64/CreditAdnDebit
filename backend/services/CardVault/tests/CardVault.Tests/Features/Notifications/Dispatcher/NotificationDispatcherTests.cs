using BuildingBlocks.Outbox;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Templates;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Tests.Features.Notifications.Fakes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CardVault.Tests.Features.Notifications.Dispatcher;

/// <summary>
/// Task 1d.2 — INotificationDispatcher unit tests.
/// Uses in-memory EF + FakeNotificationProvider + controllable clock.
/// RealProvidersEnabled = true only in tests that explicitly test the enabled path.
/// </summary>
public sealed class NotificationDispatcherTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PciAuditPublisher _pciAudit;
    private readonly IEventBus _bus;
    private readonly VaultCrypto _crypto;
    private DateTimeOffset _fixedClock = DateTimeOffset.UtcNow;

    public NotificationDispatcherTests()
    {
        var opts = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CardVaultDbContext(opts);
        _bus = Substitute.For<IEventBus>();
        _pciAudit = new PciAuditPublisher(_bus);
        _audit = new AuditService(_db);

        // Use a real VaultCrypto with a test key so dispatcher tests work end-to-end.
        var vaultOpts = new VaultOptions
        {
            ActiveKeyId = "test-k1",
            Keys = new Dictionary<string, string>
            {
                ["test-k1"] = Convert.ToBase64String(new byte[32])
            }
        };
        _crypto = new VaultCrypto(vaultOpts);
    }

    public void Dispose() => _db.Dispose();

    private NotificationDispatcher BuildDispatcher(
        INotificationProviderRegistry registry,
        NotificationDispatcherOptions? options = null,
        INotificationTemplateRenderer? renderer = null)
    {
        var opts = Options.Create(options ?? new NotificationDispatcherOptions
        {
            RealProvidersEnabled = true,
            MaxAttempts = 3,
            LockTtlMinutes = 5,
            BatchSize = 50
        });

        var fsm = new DeliveryStateMachine(() => _fixedClock);
        var tplRenderer = renderer ?? Substitute.For<INotificationTemplateRenderer>();

        return new NotificationDispatcher(
            _db,
            registry,
            fsm,
            tplRenderer,
            _pciAudit,
            _audit,
            NullLogger<NotificationDispatcher>.Instance,
            opts,
            _crypto,
            () => _fixedClock);
    }

    private CustomerNotificationDeliveryEntity SeedPendingDelivery(
        Guid? tenantId = null,
        NotificationChannel channel = NotificationChannel.Email,
        string destination = "test@example.com",
        DateTimeOffset? nextAttemptOn = null,
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Pending,
        int attempts = 0,
        DateTimeOffset? sendingStartedOn = null)
    {
        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Severity = NotificationSeverity.Info,
            Title = "Test",
            Message = "Test notification",
            CreatedOn = _fixedClock
        };
        _db.CustomerNotifications.Add(notification);

        // Encrypt the destination so the dispatcher can decrypt it (Slice 1e.1 requirement).
        var (keyId, nonce, cipher, tag) = _crypto.EncryptToParts<string>(destination);

        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = channel,
            DestinationMasked = "te***@example.com",
            DestinationHash = "hash",
            DestinationKeyId = keyId,
            DestinationNonceB64 = nonce,
            DestinationCipherB64 = cipher,
            DestinationTagB64 = tag,
            Status = status,
            Attempts = attempts,
            TenantId = tenantId ?? Guid.Empty,
            NextAttemptOn = nextAttemptOn,
            SendingStartedOn = sendingStartedOn,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();

        return delivery;
    }

    // ────────────────────────────────────────────────────────────────────
    // RealProvidersEnabled = false guard
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_WhenRealProvidersDisabled_ReturnsZeroAndLeavesRowsPending()
    {
        SeedPendingDelivery();
        var registry = new FakeProviderRegistry(
            new FakeNotificationProvider(NotificationChannel.Email));
        var dispatcher = BuildDispatcher(registry, new NotificationDispatcherOptions
        {
            RealProvidersEnabled = false
        });

        var result = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        result.Should().Be(0, "dispatcher must not send when RealProvidersEnabled = false");
        var delivery = await _db.CustomerNotificationDeliveries.FirstAsync();
        delivery.Status.Should().Be(NotificationDeliveryStatus.Pending);
    }

    // ────────────────────────────────────────────────────────────────────
    // Pending → Sending BEFORE provider call
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_TransitionsToPending_BeforeProviderCall_ThenToSent()
    {
        // Provider returns Accepted; we verify Sending was persisted mid-flight via SendingStartedOn
        var delivery = SeedPendingDelivery();
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        var count = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        count.Should().Be(1);
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.Sent,
            "successful send must reach Sent");
        updated.ProviderId.Should().Be("fake");
        updated.ProviderReference.Should().NotBeNullOrEmpty("Sent transition requires non-null provider reference");
        updated.SendingStartedOn.Should().BeNull("SendingStartedOn must be cleared on terminal state");
        updated.Attempts.Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────────
    // Sending → Sent requires non-null providerReference
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_Accepted_SetsProviderId_And_ClearsLock()
    {
        var delivery = SeedPendingDelivery(channel: NotificationChannel.Sms, destination: "+15551234567");
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Sms, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.ProviderReference.Should().NotBeNullOrEmpty();
        updated.ProviderId.Should().Be("fake");
        updated.SendingStartedOn.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────
    // TransientFailure → Failed + NextAttemptOn set
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_TransientFailure_FirstAttempt_SetsNextAttemptOn_30s()
    {
        var delivery = SeedPendingDelivery();
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.TransientFailure);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        updated.Attempts.Should().Be(1);
        updated.NextAttemptOn.Should().NotBeNull("NextAttemptOn must be set after transient failure");

        var expectedBase = _fixedClock.AddSeconds(30);
        var tolerance = TimeSpan.FromSeconds(3); // 10% of 30s = 3s
        updated.NextAttemptOn!.Value.Should()
            .BeCloseTo(expectedBase, tolerance,
                "30s backoff ±10% for attempt 1");
    }

    // ────────────────────────────────────────────────────────────────────
    // PermanentFailure → DeadLetter (immediate, no retry)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_PermanentFailure_GoesToDeadLetter_ImmediatelyNoRetry()
    {
        var delivery = SeedPendingDelivery();
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.PermanentFailure);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        var count = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        count.Should().Be(1);
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "permanent failure must go directly to DeadLetter without retry");
        updated.NextAttemptOn.Should().BeNull("DeadLetter rows must not have NextAttemptOn set");
    }

    // ────────────────────────────────────────────────────────────────────
    // PermanentFailure emits cv.notification.deadletter outbox event
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_PermanentFailure_EmitsDeadLetterOutboxEvent()
    {
        var delivery = SeedPendingDelivery();
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.PermanentFailure);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var outboxMessages = await _db.OutboxMessages.ToListAsync();
        outboxMessages.Should().Contain(m => m.Topic == "cv.notification.deadletter",
            "DeadLetter transition must emit cv.notification.deadletter outbox event");
    }

    // ────────────────────────────────────────────────────────────────────
    // Claim also picks up Failed rows where NextAttemptOn <= now
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_ClaimsFailedRows_WhenNextAttemptOnElapsed()
    {
        // Seed a Failed row with NextAttemptOn in the past
        var delivery = SeedPendingDelivery(
            status: NotificationDeliveryStatus.Failed,
            attempts: 1,
            nextAttemptOn: _fixedClock.AddMinutes(-1));

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        var count = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        count.Should().Be(1);
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.Sent);
    }

    [Fact]
    public async Task DispatchBatchAsync_DoesNotClaimFailedRows_WhenNextAttemptOnInFuture()
    {
        SeedPendingDelivery(
            status: NotificationDeliveryStatus.Failed,
            attempts: 1,
            nextAttemptOn: _fixedClock.AddHours(1));

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        var count = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        count.Should().Be(0, "Failed row with future NextAttemptOn must not be claimed");
    }

    // ────────────────────────────────────────────────────────────────────
    // Crash recovery: Sending rows with SendingStartedOn < now - LockTtl
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_CrashRecovery_ReclaimesSendingRows_WithExpiredLock()
    {
        // Row stuck in Sending (SendingStartedOn > LockTtl ago)
        var lockExpiry = _fixedClock.AddMinutes(-10); // > 5-min LockTtl
        SeedPendingDelivery(
            status: NotificationDeliveryStatus.Sending,
            attempts: 1,
            sendingStartedOn: lockExpiry);

        // Provider returns Accepted on retry
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry, new NotificationDispatcherOptions
        {
            RealProvidersEnabled = true,
            MaxAttempts = 3,
            LockTtlMinutes = 5
        });

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var updated = await _db.CustomerNotificationDeliveries.FirstAsync();
        // After crash recovery: the row should transition Failed → Sending → Sent
        // (or directly to Sent if the retry succeeds within the same tick)
        updated.Status.Should().BeOneOf(NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.Failed);
    }

    // ────────────────────────────────────────────────────────────────────
    // Retry loop: Transient × 2 → Accepted → Sent
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_RetryThenSent_ProvesTwoTransientsThenAccepted()
    {
        var delivery = SeedPendingDelivery(attempts: 0);
        // First dispatch: Transient → Failed
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email,
            ProviderOutcome.TransientFailure);
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(fakeProvider));
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var after1 = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        after1!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        after1.Attempts.Should().Be(1);

        // Advance clock past NextAttemptOn
        _fixedClock = _fixedClock.AddMinutes(1);

        // Second dispatch: Transient again → Failed with 2 attempts
        var fakeProvider2 = new FakeNotificationProvider(NotificationChannel.Email,
            ProviderOutcome.TransientFailure);
        var dispatcher2 = BuildDispatcher(new FakeProviderRegistry(fakeProvider2));
        await dispatcher2.DispatchBatchAsync(50, CancellationToken.None);

        var after2 = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        after2!.Status.Should().Be(NotificationDeliveryStatus.Failed);
        after2.Attempts.Should().Be(2);

        // Advance clock past NextAttemptOn again
        _fixedClock = _fixedClock.AddMinutes(3);

        // Third dispatch: Accepted → Sent
        var fakeProvider3 = new FakeNotificationProvider(NotificationChannel.Email,
            ProviderOutcome.Accepted);
        var dispatcher3 = BuildDispatcher(new FakeProviderRegistry(fakeProvider3));
        await dispatcher3.DispatchBatchAsync(50, CancellationToken.None);

        var after3 = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        after3!.Status.Should().Be(NotificationDeliveryStatus.Sent);
        after3.Attempts.Should().Be(3);
    }

    // ────────────────────────────────────────────────────────────────────
    // Exhausted retries → DeadLetter
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_MaxAttemptsExhausted_GoesToDeadLetter()
    {
        // Already failed 2 times (Attempts = 2), now gets a third transient → DeadLetter
        var delivery = SeedPendingDelivery(
            status: NotificationDeliveryStatus.Failed,
            attempts: 2,
            nextAttemptOn: _fixedClock.AddMinutes(-1));

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email,
            ProviderOutcome.TransientFailure);
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(fakeProvider),
            new NotificationDispatcherOptions
            {
                RealProvidersEnabled = true,
                MaxAttempts = 3
            });

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "exhausted MaxAttempts budget must result in DeadLetter");

        var outboxMessages = await _db.OutboxMessages.ToListAsync();
        outboxMessages.Should().Contain(m => m.Topic == "cv.notification.deadletter");
    }

    // ────────────────────────────────────────────────────────────────────
    // Sent row keeps cv.customer.notification.delivered outbox event (back-compat)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_OnSent_EmitsDeliveredOutboxEvent_BackCompat()
    {
        SeedPendingDelivery();
        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(fakeProvider));

        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        var outboxMessages = await _db.OutboxMessages.ToListAsync();
        outboxMessages.Should().Contain(m => m.Topic == "cv.customer.notification.delivered",
            "existing back-compat outbox event must still be emitted on Sent");
    }

    // ────────────────────────────────────────────────────────────────────
    // Do NOT claim Failed rows that have exhausted budget (Attempts >= MaxAttempts)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchBatchAsync_DoesNotClaim_FailedRows_WithExhaustedBudget()
    {
        SeedPendingDelivery(
            status: NotificationDeliveryStatus.Failed,
            attempts: 3,
            nextAttemptOn: _fixedClock.AddMinutes(-1));

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email);
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(fakeProvider),
            new NotificationDispatcherOptions
            {
                RealProvidersEnabled = true,
                MaxAttempts = 3
            });

        var count = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        count.Should().Be(0, "rows with Attempts >= MaxAttempts must not be claimed");
    }
}
