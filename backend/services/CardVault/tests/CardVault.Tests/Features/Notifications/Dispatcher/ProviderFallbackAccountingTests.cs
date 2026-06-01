using BuildingBlocks.Outbox;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Templates;
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
/// Task 1d.4 — Provider fallback chain accounting tests.
/// Verifies that:
/// - Attempts is incremented per provider attempt within a single dispatch tick.
/// - When the first provider returns TransientFailure the dispatcher falls through
///   to the next provider in the chain without returning early.
/// - PermanentFailure from any provider short-circuits the chain immediately (no fallback).
/// - The MaxAttempts budget gate deadletters when all providers in the chain are exhausted.
/// </summary>
public sealed class ProviderFallbackAccountingTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PciAuditPublisher _pciAudit;
    private readonly IEventBus _bus;
    private readonly DateTimeOffset _fixedClock = DateTimeOffset.UtcNow;

    public ProviderFallbackAccountingTests()
    {
        var opts = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CardVaultDbContext(opts);
        _bus = Substitute.For<IEventBus>();
        _pciAudit = new PciAuditPublisher(_bus);
        _audit = new AuditService(_db);
    }

    public void Dispose() => _db.Dispose();

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private NotificationDispatcher BuildDispatcher(
        INotificationProviderRegistry registry,
        int maxAttempts = 3)
    {
        var opts = Options.Create(new NotificationDispatcherOptions
        {
            RealProvidersEnabled = true,
            MaxAttempts = maxAttempts,
            LockTtlMinutes = 5,
            BatchSize = 50
        });

        var fsm = new DeliveryStateMachine(() => _fixedClock);
        var renderer = Substitute.For<INotificationTemplateRenderer>();

        return new NotificationDispatcher(
            _db,
            registry,
            fsm,
            renderer,
            _pciAudit,
            _audit,
            NullLogger<NotificationDispatcher>.Instance,
            opts,
            () => _fixedClock);
    }

    private CustomerNotificationDeliveryEntity SeedPendingDelivery(
        NotificationChannel channel = NotificationChannel.Email)
    {
        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Severity = NotificationSeverity.Info,
            Title = "Test",
            Message = "Test message",
            CreatedOn = _fixedClock
        };
        _db.CustomerNotifications.Add(notification);

        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = channel,
            DestinationMasked = "te***@example.com",
            DestinationHash = "hash",
            Status = NotificationDeliveryStatus.Pending,
            Attempts = 0,
            TenantId = Guid.NewGuid(),
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();
        return delivery;
    }

    // ────────────────────────────────────────────────────────────────────
    // Transient → Accepted in two-provider chain: Attempts=2, Status=Sent
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fallback_FirstTransient_SecondAccepted_StatusSent_Attempts2()
    {
        // Arrange
        var providerA = new FakeNotificationProvider(NotificationChannel.Email, "provider-a",
            ProviderOutcome.TransientFailure);
        var providerB = new FakeNotificationProvider(NotificationChannel.Email, "provider-b",
            ProviderOutcome.Accepted);

        var delivery = SeedPendingDelivery();
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(providerA, providerB), maxAttempts: 3);

        // Act
        var terminal = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        terminal.Should().Be(1, "delivery reached a terminal state");

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.Sent,
            "second provider succeeded — delivery must be Sent");
        updated.Attempts.Should().Be(2,
            "one attempt per provider: providerA (transient) + providerB (accepted)");
        updated.ProviderId.Should().Be("provider-b",
            "the provider that accepted the send wins");
        updated.ProviderReference.Should().NotBeNullOrEmpty(
            "Sent state requires a non-null provider reference");

        providerA.Calls.Should().HaveCount(1, "providerA was tried once before fallback");
        providerB.Calls.Should().HaveCount(1, "providerB accepted on first attempt");
    }

    // ────────────────────────────────────────────────────────────────────
    // All providers transient + MaxAttempts=2 → DeadLetter after 2 attempts
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fallback_AllTransient_BudgetExhausted_DeadLetter()
    {
        // Arrange — MaxAttempts=2, two transient providers exhaust the budget in one tick
        var providerA = new FakeNotificationProvider(NotificationChannel.Email, "provider-a",
            ProviderOutcome.TransientFailure);
        var providerB = new FakeNotificationProvider(NotificationChannel.Email, "provider-b",
            ProviderOutcome.TransientFailure);

        var delivery = SeedPendingDelivery();
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(providerA, providerB), maxAttempts: 2);

        // Act
        var terminal = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        terminal.Should().Be(1, "delivery was dead-lettered — terminal state reached");

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "both providers failed transiently and the MaxAttempts=2 budget was exhausted");
        updated.Attempts.Should().Be(2,
            "providerA (attempt 1) + providerB (attempt 2) exhausted the budget");

        providerA.Calls.Should().HaveCount(1, "providerA was tried once");
        providerB.Calls.Should().HaveCount(1, "providerB was tried once before budget ran out");
    }

    // ────────────────────────────────────────────────────────────────────
    // PermanentFailure from first provider short-circuits — second never called
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fallback_FirstPermanent_NoFallback_DeadLetter_SecondNotCalled()
    {
        // Arrange — first provider returns PermanentFailure; second should never be invoked
        var providerA = new FakeNotificationProvider(NotificationChannel.Email, "provider-a",
            ProviderOutcome.PermanentFailure);
        var providerB = new FakeNotificationProvider(NotificationChannel.Email, "provider-b",
            ProviderOutcome.Accepted);

        var delivery = SeedPendingDelivery();
        var dispatcher = BuildDispatcher(new FakeProviderRegistry(providerA, providerB), maxAttempts: 3);

        // Act
        var terminal = await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        terminal.Should().Be(1, "permanent failure is a terminal state");

        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "permanent failure must deadletter immediately — no fallback allowed");
        updated.Attempts.Should().Be(1,
            "only providerA was attempted before the chain was short-circuited");
        updated.ProviderId.Should().Be("provider-a",
            "the failing provider ID must be recorded for diagnostics");

        providerA.Calls.Should().HaveCount(1, "providerA was the only provider called");
        providerB.Calls.Should().BeEmpty(
            "providerB must never be called when a PermanentFailure short-circuits the chain");
    }
}
