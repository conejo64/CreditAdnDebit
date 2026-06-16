using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Templates;
using CardVault.Api.Vault;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Tests.Features.Notifications.Fakes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BuildingBlocks.Outbox;

namespace CardVault.Tests.Features.Notifications.Dispatcher;

/// <summary>
/// Slice 1e.1 — Encrypted destination snapshot tests.
/// RED → GREEN TDD for the bug fix: dispatcher was sending masked destination to providers.
///
/// Four scenarios:
///   1. Enqueue: encrypted parts are non-null and decrypt back to original value (roundtrip).
///   2. Dispatch: provider receives the REAL (decrypted) destination in NotificationSendRequest.Destination.
///   3. Fail-closed: legacy row (null encrypted parts) goes to DeadLetter, no provider send.
///   4. Guard: masked value NEVER reaches the provider's Destination field.
/// </summary>
public sealed class EncryptedDestinationTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly VaultCrypto _crypto;
    private readonly AuditService _audit;
    private readonly PciAuditPublisher _pciAudit;
    private DateTimeOffset _fixedClock = DateTimeOffset.UtcNow;

    public EncryptedDestinationTests()
    {
        var opts = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CardVaultDbContext(opts);

        var bus = Substitute.For<IEventBus>();
        _pciAudit = new PciAuditPublisher(bus);
        _audit = new AuditService(_db);

        // Create a real VaultCrypto with a known 32-byte test key
        var vaultOpts = new VaultOptions
        {
            ActiveKeyId = "test-k1",
            Keys = new Dictionary<string, string>
            {
                ["test-k1"] = Convert.ToBase64String(new byte[32]) // 32 zero-bytes = valid AES-256 key
            }
        };
        _crypto = new VaultCrypto(vaultOpts);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private CustomerEntity SeedCustomer(string email = "real@example.com", string phone = "+15559990000")
    {
        var customer = new CustomerEntity
        {
            Id = Guid.NewGuid(),
            CustomerNumber = Guid.NewGuid().ToString("N")[..12],
            FullName = "Test Customer",
            DocumentId = Guid.NewGuid().ToString("N")[..10],
            Email = email,
            Phone = phone
        };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        return customer;
    }

    private NotificationService BuildNotificationService()
        => new(_db, _audit, _pciAudit, _crypto);

    private NotificationDispatcher BuildDispatcher(
        INotificationProviderRegistry registry,
        NotificationDispatcherOptions? options = null)
    {
        var dispatcherOpts = Options.Create(options ?? new NotificationDispatcherOptions
        {
            RealProvidersEnabled = true,
            MaxAttempts = 3,
            LockTtlMinutes = 5,
            BatchSize = 50
        });

        var fsm = new DeliveryStateMachine(() => _fixedClock);
        var renderer = Substitute.For<INotificationTemplateRenderer>();
        renderer.RenderAsync(Arg.Any<TemplateModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RenderedTemplate?>(new RenderedTemplate("Subject", "Body")));

        return new NotificationDispatcher(
            _db,
            registry,
            fsm,
            renderer,
            _pciAudit,
            _audit,
            NullLogger<NotificationDispatcher>.Instance,
            dispatcherOpts,
            _crypto,
            () => _fixedClock);
    }

    /// <summary>
    /// Seeds a delivery with encrypted destination parts set (simulates a post-1e.1 row).
    /// </summary>
    private CustomerNotificationDeliveryEntity SeedEncryptedDelivery(
        string realDestination,
        NotificationChannel channel = NotificationChannel.Email)
    {
        var (keyId, nonce, cipher, tag) = _crypto.EncryptToParts<string>(realDestination);

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
            Status = NotificationDeliveryStatus.Pending,
            TenantId = Guid.Empty,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();
        return delivery;
    }

    /// <summary>
    /// Seeds a legacy delivery with NO encrypted parts (simulates a pre-1e.1 row).
    /// </summary>
    private CustomerNotificationDeliveryEntity SeedLegacyDelivery(
        NotificationChannel channel = NotificationChannel.Email)
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

        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = channel,
            DestinationMasked = "te***@example.com",
            DestinationHash = "hash",
            // Deliberately null — legacy row
            DestinationKeyId = null,
            DestinationNonceB64 = null,
            DestinationCipherB64 = null,
            DestinationTagB64 = null,
            Status = NotificationDeliveryStatus.Pending,
            TenantId = Guid.Empty,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();
        return delivery;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 1 — Enqueue: encrypted parts are persisted and roundtrip correctly
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateNotification_Email_PersistsEncryptedParts_And_DecryptsToOriginalEmail()
    {
        // Arrange
        const string realEmail = "alice@example.com";
        var customer = SeedCustomer(email: realEmail, phone: "+15550000001");
        var account = new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            ProductCode = "GOLD",
            AccountNumber = $"ACCT{Guid.NewGuid():N}"[..12],
            AccountType = AccountType.Credit
        };
        _db.Accounts.Add(account);
        _db.SaveChanges();

        var svc = BuildNotificationService();

        // Act
        await svc.CreateTransactionNotificationAsync(
            account.Id, null, 100m, "USD", "TestMerchant",
            "test.event", "trace-001", CancellationToken.None);

        // Assert
        var deliveries = await _db.CustomerNotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Email)
            .ToListAsync();

        deliveries.Should().HaveCount(1);
        var d = deliveries[0];

        d.DestinationKeyId.Should().NotBeNullOrEmpty("encrypted parts must be stored at enqueue");
        d.DestinationNonceB64.Should().NotBeNullOrEmpty();
        d.DestinationCipherB64.Should().NotBeNullOrEmpty();
        d.DestinationTagB64.Should().NotBeNullOrEmpty();

        // Roundtrip: decrypt back to the real email
        var decrypted = _crypto.DecryptFromParts<string>(
            d.DestinationKeyId!,
            d.DestinationNonceB64!,
            d.DestinationCipherB64!,
            d.DestinationTagB64!);

        decrypted.Should().Be(realEmail, "decrypted value must equal the original real email");
    }

    [Fact]
    public async Task CreateNotification_Sms_PersistsEncryptedParts_And_DecryptsToOriginalPhone()
    {
        // Arrange — triangulation: same scenario for SMS channel
        const string realPhone = "+15550000042";
        var customer = SeedCustomer(email: "bob@example.com", phone: realPhone);
        var account = new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            ProductCode = "GOLD",
            AccountNumber = $"ACCT{Guid.NewGuid():N}"[..12],
            AccountType = AccountType.Credit
        };
        _db.Accounts.Add(account);
        _db.SaveChanges();

        var svc = BuildNotificationService();

        // Act
        await svc.CreateTransactionNotificationAsync(
            account.Id, null, 50m, "USD", "Merchant2",
            "test.event2", "trace-002", CancellationToken.None);

        // Assert
        var deliveries = await _db.CustomerNotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Sms)
            .ToListAsync();

        deliveries.Should().HaveCount(1);
        var d = deliveries[0];

        d.DestinationKeyId.Should().NotBeNullOrEmpty();
        d.DestinationNonceB64.Should().NotBeNullOrEmpty();
        d.DestinationCipherB64.Should().NotBeNullOrEmpty();
        d.DestinationTagB64.Should().NotBeNullOrEmpty();

        var decrypted = _crypto.DecryptFromParts<string>(
            d.DestinationKeyId!,
            d.DestinationNonceB64!,
            d.DestinationCipherB64!,
            d.DestinationTagB64!);

        decrypted.Should().Be(realPhone, "decrypted value must equal the original real phone");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 2 — Dispatch: provider receives the REAL destination
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchDelivery_WithEncryptedParts_ProviderReceivesRealDestination()
    {
        // Arrange
        const string realEmail = "carol@example.com";
        var delivery = SeedEncryptedDelivery(realEmail, NotificationChannel.Email);

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        fakeProvider.Calls.Should().HaveCount(1, "provider must be called exactly once");
        fakeProvider.Calls[0].Destination.Should().Be(realEmail,
            "provider must receive the real (decrypted) destination, not the masked value");
    }

    [Fact]
    public async Task DispatchDelivery_SmsChannel_ProviderReceivesRealPhone()
    {
        // Arrange — triangulation: same scenario for SMS
        const string realPhone = "+15559991111";
        var delivery = SeedEncryptedDelivery(realPhone, NotificationChannel.Sms);

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Sms, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        fakeProvider.Calls.Should().HaveCount(1);
        fakeProvider.Calls[0].Destination.Should().Be(realPhone,
            "SMS provider must receive the real E.164 phone number, not the masked value");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 3 — Fail-closed: legacy row goes to DeadLetter, no provider send
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchDelivery_LegacyRow_NullEncryptedParts_GoesToDeadLetter_NoProviderCall()
    {
        // Arrange: legacy row without encrypted parts
        var delivery = SeedLegacyDelivery(NotificationChannel.Email);

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert: DeadLetter — NOT sent
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "legacy rows without encrypted destination must fail-closed to DeadLetter");
        updated.LastError.Should().Be("missing-encrypted-destination",
            "fail-closed reason must identify the missing encrypted destination");

        // CRITICAL: no provider send was attempted
        fakeProvider.Calls.Should().BeEmpty(
            "the provider must NEVER be called when encrypted destination parts are missing");
    }

    [Fact]
    public async Task DispatchDelivery_PartialEncryptedParts_GoesToDeadLetter_NoProviderCall()
    {
        // Arrange — triangulation: only KeyId set, rest null
        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Severity = NotificationSeverity.Info,
            Title = "Test",
            Message = "Test",
            CreatedOn = _fixedClock
        };
        _db.CustomerNotifications.Add(notification);
        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = NotificationChannel.Email,
            DestinationMasked = "te***@example.com",
            DestinationHash = "hash",
            DestinationKeyId = "test-k1",   // only KeyId — rest null → partial/corrupt
            DestinationNonceB64 = null,
            DestinationCipherB64 = null,
            DestinationTagB64 = null,
            Status = NotificationDeliveryStatus.Pending,
            TenantId = Guid.Empty,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "partial encrypted destination must also fail-closed");
        fakeProvider.Calls.Should().BeEmpty("no provider send on partial encrypted parts");
    }

    [Fact]
    public async Task DispatchDelivery_CorruptCiphertext_DecryptThrows_GoesToDeadLetter_NoProviderCall()
    {
        // Arrange — all four parts present, but the tag is tampered so AesGcm.Decrypt throws.
        // Simulates a corrupted row or a ciphertext encrypted under a now-retired key.
        const string realEmail = "erin@example.com";
        var (keyId, nonce, cipher, _) = _crypto.EncryptToParts<string>(realEmail);

        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Severity = NotificationSeverity.Info,
            Title = "Test",
            Message = "Test",
            CreatedOn = _fixedClock
        };
        _db.CustomerNotifications.Add(notification);
        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = NotificationChannel.Email,
            DestinationMasked = "er***@example.com",
            DestinationHash = "hash",
            DestinationKeyId = keyId,
            DestinationNonceB64 = nonce,
            DestinationCipherB64 = cipher,
            DestinationTagB64 = Convert.ToBase64String(new byte[16]), // bogus tag → auth failure
            Status = NotificationDeliveryStatus.Pending,
            TenantId = Guid.Empty,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert: a decrypt failure must fail-closed IMMEDIATELY to DeadLetter — not get stuck
        // in Sending and burn the whole retry budget via crash-recovery.
        var updated = await _db.CustomerNotificationDeliveries.FindAsync(delivery.Id);
        updated!.Status.Should().Be(NotificationDeliveryStatus.DeadLetter,
            "an undecryptable destination must fail-closed to DeadLetter on the first tick");
        updated.LastError.Should().Be("missing-encrypted-destination",
            "decrypt failure shares the missing/corrupt fail-closed reason");
        fakeProvider.Calls.Should().BeEmpty(
            "the provider must NEVER be called when the destination cannot be decrypted");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test 4 — Guard: masked value NEVER reaches the provider's Destination
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchDelivery_MaskedValueNeverReachesProvider()
    {
        // Arrange: real destination differs from masked
        const string realEmail = "dave@example.com";
        const string maskedEmail = "da***@example.com";

        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Severity = NotificationSeverity.Info,
            Title = "Test",
            Message = "Test",
            CreatedOn = _fixedClock
        };
        _db.CustomerNotifications.Add(notification);

        var (keyId, nonce, cipher, tag) = _crypto.EncryptToParts<string>(realEmail);
        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Notification = notification,
            Channel = NotificationChannel.Email,
            DestinationMasked = maskedEmail,   // deliberately different from real
            DestinationHash = "hash",
            DestinationKeyId = keyId,
            DestinationNonceB64 = nonce,
            DestinationCipherB64 = cipher,
            DestinationTagB64 = tag,
            Status = NotificationDeliveryStatus.Pending,
            TenantId = Guid.Empty,
            CreatedOn = _fixedClock
        };
        _db.CustomerNotificationDeliveries.Add(delivery);
        _db.SaveChanges();

        var fakeProvider = new FakeNotificationProvider(NotificationChannel.Email, ProviderOutcome.Accepted);
        var registry = new FakeProviderRegistry(fakeProvider);
        var dispatcher = BuildDispatcher(registry);

        // Act
        await dispatcher.DispatchBatchAsync(50, CancellationToken.None);

        // Assert
        fakeProvider.Calls.Should().HaveCount(1);
        var capturedDestination = fakeProvider.Calls[0].Destination;

        capturedDestination.Should().Be(realEmail,
            "the provider must receive the real destination");
        capturedDestination.Should().NotBe(maskedEmail,
            "the MASKED value must NEVER reach the provider's Destination field");
    }
}
