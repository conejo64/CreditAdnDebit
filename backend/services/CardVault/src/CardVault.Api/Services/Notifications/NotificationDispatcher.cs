using System.Security.Cryptography;
using System.Text.Json;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications.Templates;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardVault.Api.Services.Notifications;

/// <summary>
/// Claims a batch of pending/failed deliveries, drives the 5-state FSM, calls providers,
/// and persists each transition. Scoped — one instance per dispatcher tick.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    // Outbox topics
    private const string DeliveredTopic = "cv.customer.notification.delivered";
    private const string DeadLetterTopic = "cv.notification.deadletter";

    // PCI audit event types
    private const string PciSendAttempt = "pci.notification.send-attempt";
    private const string PciSendResult = "pci.notification.send-result";
    private const string PciDeadLetter = "pci.notification.deadletter";
    private const string PciDelivered = "pci.notification.delivered";          // back-compat (ADR-9)
    private const string PciDeliveryConfirmed = "pci.notification.delivery-confirmed"; // Slice 2a: degraded path

    private readonly CardVaultDbContext _db;
    private readonly INotificationProviderRegistry _registry;
    private readonly IDeliveryStateMachine _fsm;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly PciAuditPublisher _pciAudit;
    private readonly AuditService _audit;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly NotificationDispatcherOptions _options;
    private readonly VaultCrypto _crypto;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// DI constructor — uses <see cref="DateTimeOffset.UtcNow"/> as the clock.
    /// </summary>
    public NotificationDispatcher(
        CardVaultDbContext db,
        INotificationProviderRegistry registry,
        IDeliveryStateMachine fsm,
        INotificationTemplateRenderer renderer,
        PciAuditPublisher pciAudit,
        AuditService audit,
        ILogger<NotificationDispatcher> logger,
        IOptions<NotificationDispatcherOptions> options,
        VaultCrypto crypto)
        : this(db, registry, fsm, renderer, pciAudit, audit, logger, options, crypto,
               () => DateTimeOffset.UtcNow) { }

    /// <summary>
    /// Full constructor — accepts an injectable clock for deterministic testing.
    /// In production, prefer the DI constructor (without clock) which uses <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public NotificationDispatcher(
        CardVaultDbContext db,
        INotificationProviderRegistry registry,
        IDeliveryStateMachine fsm,
        INotificationTemplateRenderer renderer,
        PciAuditPublisher pciAudit,
        AuditService audit,
        ILogger<NotificationDispatcher> logger,
        IOptions<NotificationDispatcherOptions> options,
        VaultCrypto crypto,
        Func<DateTimeOffset> clock)
    {
        _db = db;
        _registry = registry;
        _fsm = fsm;
        _renderer = renderer;
        _pciAudit = pciAudit;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
        _crypto = crypto;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<int> DispatchBatchAsync(int take, CancellationToken ct)
    {
        // Guard: real providers must be explicitly enabled
        if (!_options.RealProvidersEnabled)
            return 0;

        var limit = take <= 0 ? _options.BatchSize : Math.Min(take, 200);
        var now = _clock();
        var lockExpiry = now.AddMinutes(-_options.LockTtlMinutes);

        // ── Step 1: Crash recovery — reclaim rows stuck in Sending (lock expired) ──
        await ReclaimSendingRowsAsync(lockExpiry, ct);

        // ── Step 2: Claim batch (Pending + eligible Failed) ──
        var deliveries = await _db.CustomerNotificationDeliveries
            .Include(x => x.Notification)
            .Where(x =>
                x.Status == NotificationDeliveryStatus.Pending
                || (x.Status == NotificationDeliveryStatus.Failed
                    && x.NextAttemptOn <= now
                    && x.Attempts < _options.MaxAttempts))
            .OrderBy(x => x.CreatedOn)
            .Take(limit)
            .ToListAsync(ct);

        var terminal = 0;
        foreach (var delivery in deliveries)
        {
            try
            {
                var reached = await DispatchDeliveryAsync(delivery, now, ct);
                if (reached)
                    terminal++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception dispatching delivery {DeliveryId}", delivery.Id);
            }
        }

        return terminal;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reclaims rows stuck in Sending (SendingStartedOn older than LockTtl).
    /// Sets them to Failed with a crash-recovery error message.
    /// </summary>
    private async Task ReclaimSendingRowsAsync(DateTimeOffset lockExpiry, CancellationToken ct)
    {
        var stuck = await _db.CustomerNotificationDeliveries
            .Where(x => x.Status == NotificationDeliveryStatus.Sending
                        && x.SendingStartedOn < lockExpiry)
            .ToListAsync(ct);

        foreach (var row in stuck)
        {
            row.Attempts++;
            row.LastError = "dispatcher-crash-recovery";
            _fsm.Transition(row, NotificationDeliveryStatus.Failed);
            row.NextAttemptOn = row.Attempts < _options.MaxAttempts
                ? _fsm.ComputeNextAttempt(row.Attempts, DateTimeOffset.UtcNow)
                : null;

            _logger.LogWarning(
                "Crash-recovery: delivery {DeliveryId} reclaimed from Sending (locked since {SendingStartedOn})",
                row.Id, row.SendingStartedOn);
        }

        if (stuck.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Dispatches a single delivery through the provider chain.
    /// Returns true when the delivery reached a terminal state (Sent or DeadLetter).
    /// </summary>
    private async Task<bool> DispatchDeliveryAsync(
        CustomerNotificationDeliveryEntity delivery,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Transition Pending|Failed → Sending (persists the lock timestamp)
        _fsm.Transition(delivery, NotificationDeliveryStatus.Sending);
        await _db.SaveChangesAsync(ct);

        // Emit PCI send-attempt event (BEFORE provider call)
        await _pciAudit.PublishAsync(PciSendAttempt, delivery.NotificationId.ToString("N"), new
        {
            deliveryId = delivery.Id,
            notificationId = delivery.NotificationId,
            tenantId = delivery.TenantId,
            channel = delivery.Channel.ToString(),
            attempts = delivery.Attempts,
            at = now
        }, ct);

        // Resolve real destination — fail-closed if encrypted snapshot is missing or corrupt
        var destination = GetUnmaskedDestination(delivery);
        if (destination is null)
        {
            _logger.LogError(
                "Delivery {DeliveryId} has missing or corrupt encrypted destination parts — routing to DeadLetter (fail-closed)",
                delivery.Id);
            await SetDeadLetterAsync(delivery, "missing-encrypted-destination", null, null, ct);
            return true;
        }

        // Resolve the provider chain
        var chain = _registry.ResolveChain(delivery.TenantId, delivery.Channel, destination);

        if (chain.Count == 0)
        {
            _logger.LogWarning(
                "No provider chain found for delivery {DeliveryId} (channel={Channel})",
                delivery.Id, delivery.Channel);
            await SetDeadLetterAsync(delivery, "no-provider-chain", null, null, ct);
            return true;
        }

        // Render the template (subject + body)
        RenderedTemplate? rendered = null;
        try
        {
            rendered = await RenderTemplateAsync(delivery, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template render failed for delivery {DeliveryId}", delivery.Id);
            await SetDeadLetterAsync(delivery, "template-render-failed", null, null, ct);
            return true;
        }

        // Try each provider in chain (shared Attempts budget across entire chain)
        foreach (var provider in chain)
        {
            if (delivery.Attempts >= _options.MaxAttempts)
            {
                // Budget exhausted — DeadLetter
                await SetDeadLetterAsync(delivery, "max-attempts-exhausted", provider.ProviderId, null, ct);
                return true;
            }

            delivery.Attempts++;
            delivery.LastAttemptOn = now;

            var request = new NotificationSendRequest(
                DeliveryId: delivery.Id,
                TenantId: delivery.TenantId,
                Channel: delivery.Channel,
                Destination: destination,
                RenderedSubject: rendered.Subject,
                RenderedBody: rendered.Body,
                TemplateType: delivery.Notification?.Type.ToString() ?? "Transaction",
                Locale: "es-EC");

            ProviderSendResult result;
            try
            {
                result = await provider.SendAsync(request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Provider {ProviderId} threw for delivery {DeliveryId}", provider.ProviderId, delivery.Id);
                result = new ProviderSendResult(
                    ProviderOutcome.TransientFailure, null, "provider-exception", ex.Message, null);
            }

            // Emit PCI send-result event
            await _pciAudit.PublishAsync(PciSendResult, delivery.NotificationId.ToString("N"), new
            {
                deliveryId = delivery.Id,
                notificationId = delivery.NotificationId,
                tenantId = delivery.TenantId,
                channel = delivery.Channel.ToString(),
                providerId = provider.ProviderId,
                outcome = result.Outcome.ToString(),
                errorCode = result.ErrorCode,
                providerReference = result.ProviderReference,
                attempts = delivery.Attempts,
                at = now
            }, ct);

            switch (result.Outcome)
            {
                case ProviderOutcome.Accepted:
                    // Guard: Sending→Sent requires a non-null providerReference
                    if (string.IsNullOrWhiteSpace(result.ProviderReference))
                    {
                        _logger.LogError(
                            "Provider {ProviderId} returned Accepted but ProviderReference is null for delivery {DeliveryId}",
                            provider.ProviderId, delivery.Id);
                        await SetDeadLetterAsync(delivery, "accepted-without-provider-reference",
                            provider.ProviderId, null, ct);
                        return true;
                    }

                    delivery.ProviderId = provider.ProviderId;
                    delivery.ProviderReference = result.ProviderReference;
                    delivery.LastError = null;

                    // Degraded confirmation: provider signals immediate delivery (no DLR callback expected).
                    // Set DeliveredOn now so EmitSentEventsAsync captures the correct timestamp.
                    if (result.ProviderReportedAt.HasValue)
                    {
                        delivery.DeliveredOn = result.ProviderReportedAt;
                        await EmitDeliveryConfirmedEventAsync(delivery, ct);
                    }

                    _fsm.Transition(delivery, NotificationDeliveryStatus.Sent);
                    await EmitSentEventsAsync(delivery, ct);
                    await _db.SaveChangesAsync(ct);
                    return true;

                case ProviderOutcome.PermanentFailure:
                    delivery.LastError = result.ErrorMessage ?? result.ErrorCode ?? "permanent-failure";
                    delivery.ProviderId = provider.ProviderId;
                    await SetDeadLetterAsync(delivery, delivery.LastError, provider.ProviderId, result, ct);
                    return true;

                case ProviderOutcome.TransientFailure:
                    delivery.LastError = result.ErrorMessage ?? result.ErrorCode ?? "transient-failure";
                    // If budget not yet exhausted AND there are more providers, try next provider
                    // If this was the last provider in chain, fall through to Failed+backoff
                    var isLastProvider = provider == chain[^1];
                    if (!isLastProvider && delivery.Attempts < _options.MaxAttempts)
                    {
                        // Try next provider (don't save yet — continue loop)
                        continue;
                    }

                    // Set to Failed with backoff
                    _fsm.Transition(delivery, NotificationDeliveryStatus.Failed);
                    delivery.NextAttemptOn = delivery.Attempts < _options.MaxAttempts
                        ? _fsm.ComputeNextAttempt(delivery.Attempts, now)
                        : null;

                    if (delivery.Attempts >= _options.MaxAttempts)
                    {
                        // Budget exhausted after this failure → DeadLetter
                        delivery.NextAttemptOn = null;
                        _fsm.Transition(delivery, NotificationDeliveryStatus.DeadLetter);
                        await EmitDeadLetterEventAsync(delivery, ct);
                        await _db.SaveChangesAsync(ct);
                        return true;
                    }

                    await _db.SaveChangesAsync(ct);
                    return false; // Not terminal yet — retry on next tick
            }
        }

        // Chain exhausted without terminal result (shouldn't happen in normal flow)
        await SetDeadLetterAsync(delivery, "chain-exhausted", null, null, ct);
        return true;
    }

    private async Task SetDeadLetterAsync(
        CustomerNotificationDeliveryEntity delivery,
        string reason,
        string? providerId,
        ProviderSendResult? result,
        CancellationToken ct)
    {
        if (delivery.Status != NotificationDeliveryStatus.DeadLetter)
        {
            delivery.LastError = reason;
            if (providerId != null)
                delivery.ProviderId = providerId;
            delivery.NextAttemptOn = null;

            // Must transition from current state to DeadLetter
            // If currently Sending: Sending → DeadLetter
            // If currently Failed: Failed → DeadLetter
            if (_fsm.CanTransition(delivery.Status, NotificationDeliveryStatus.DeadLetter))
                _fsm.Transition(delivery, NotificationDeliveryStatus.DeadLetter);
            else if (delivery.Status == NotificationDeliveryStatus.Sending)
            {
                _fsm.Transition(delivery, NotificationDeliveryStatus.Failed);
                _fsm.Transition(delivery, NotificationDeliveryStatus.DeadLetter);
            }
        }

        await EmitDeadLetterEventAsync(delivery, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EmitSentEventsAsync(CustomerNotificationDeliveryEntity delivery, CancellationToken ct)
    {
        var notification = delivery.Notification;

        // Back-compat outbox event (ADR-9: keep existing topic)
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = DeliveredTopic,
            Key = delivery.NotificationId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                notificationId = delivery.NotificationId,
                deliveryId = delivery.Id,
                channel = delivery.Channel.ToString(),
                destinationMasked = delivery.DestinationMasked,
                deliveredOn = delivery.DeliveredOn,
                providerReference = delivery.ProviderReference,
                notificationType = notification?.Type.ToString(),
                severity = notification?.Severity.ToString(),
                traceId = notification?.TraceId
            })
        });

        // PCI back-compat event (ADR-9)
        await _pciAudit.PublishAsync(PciDelivered, delivery.NotificationId.ToString("N"), new
        {
            deliveryId = delivery.Id,
            notificationId = delivery.NotificationId,
            channel = delivery.Channel.ToString(),
            destinationMasked = delivery.DestinationMasked,
            providerReference = delivery.ProviderReference,
            traceId = notification?.TraceId
        }, ct);
    }

    private async Task EmitDeliveryConfirmedEventAsync(
        CustomerNotificationDeliveryEntity delivery,
        CancellationToken ct)
    {
        // Emitted when the provider reports immediate delivery (degraded confirmation — no DLR callback).
        // This is distinct from the back-compat pci.notification.delivered event (which is always emitted).
        await _pciAudit.PublishAsync(PciDeliveryConfirmed, delivery.NotificationId.ToString("N"), new
        {
            deliveryId = delivery.Id,
            notificationId = delivery.NotificationId,
            tenantId = delivery.TenantId,
            channel = delivery.Channel.ToString(),
            providerId = delivery.ProviderId,
            providerReference = delivery.ProviderReference,
            deliveredOn = delivery.DeliveredOn,
            traceId = delivery.Notification?.TraceId,
            degradedConfirmation = true
        }, ct);
    }

    private async Task EmitDeadLetterEventAsync(
        CustomerNotificationDeliveryEntity delivery,
        CancellationToken ct)
    {
        // Outbox event for downstream consumers
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = DeadLetterTopic,
            Key = delivery.NotificationId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                deliveryId = delivery.Id,
                notificationId = delivery.NotificationId,
                tenantId = delivery.TenantId,
                channel = delivery.Channel.ToString(),
                providerId = delivery.ProviderId,
                attempts = delivery.Attempts,
                lastError = delivery.LastError,
                at = DateTimeOffset.UtcNow
            })
        });

        // PCI audit event
        await _pciAudit.PublishAsync(PciDeadLetter, delivery.NotificationId.ToString("N"), new
        {
            deliveryId = delivery.Id,
            notificationId = delivery.NotificationId,
            tenantId = delivery.TenantId,
            channel = delivery.Channel.ToString(),
            providerId = delivery.ProviderId,
            attempts = delivery.Attempts,
            lastError = delivery.LastError,
            at = DateTimeOffset.UtcNow
        }, ct);
    }

    private async Task<RenderedTemplate> RenderTemplateAsync(
        CustomerNotificationDeliveryEntity delivery,
        CancellationToken ct)
    {
        var notification = delivery.Notification;
        if (notification is null)
        {
            // Minimal fallback when notification not loaded
            return new RenderedTemplate("Notification", "Please check your account.");
        }

        var templateType = notification.Type switch
        {
            CustomerNotificationType.Transaction => "TransactionNotification",
            CustomerNotificationType.SecurityAlert => "SecurityAlert",
            _ => "TransactionNotification"
        };

        var model = new TemplateModel(
            MaskedPan: null,
            Amount: notification.Amount,
            CurrencyCode: notification.CurrencyCode,
            MaskedMerchant: notification.MerchantName,
            Timestamp: notification.CreatedOn,
            OtpCode: null,
            TemplateType: templateType,
            Locale: "es-EC",
            AdditionalData: null);

        return await _renderer.RenderAsync(model, ct)
            ?? new RenderedTemplate("Notification", "Please check your account.");
    }

    /// <summary>
    /// Decrypts the real (unmasked) destination from the encrypted snapshot stored on the delivery.
    /// Returns <c>null</c> when any of the four encrypted parts is missing (legacy or corrupt row).
    /// Callers MUST treat a null return as a fail-closed signal — route to DeadLetter immediately,
    /// NEVER fall back to <see cref="CustomerNotificationDeliveryEntity.DestinationMasked"/>.
    /// </summary>
    private string? GetUnmaskedDestination(CustomerNotificationDeliveryEntity delivery)
    {
        // Fail-closed: all four parts must be present for decryption.
        // Legacy rows (created before Slice 1e.1) will have null parts.
        if (string.IsNullOrEmpty(delivery.DestinationKeyId)
            || string.IsNullOrEmpty(delivery.DestinationNonceB64)
            || string.IsNullOrEmpty(delivery.DestinationCipherB64)
            || string.IsNullOrEmpty(delivery.DestinationTagB64))
        {
            return null;
        }

        try
        {
            return _crypto.DecryptFromParts<string>(
                delivery.DestinationKeyId,
                delivery.DestinationNonceB64,
                delivery.DestinationCipherB64,
                delivery.DestinationTagB64);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
        {
            // Decryption failed: corrupted ciphertext/tag, or the snapshot was encrypted under a
            // key that has since been retired. Fail closed — return null so the caller routes the
            // delivery to DeadLetter immediately, rather than letting the exception escape and the
            // row get stuck in Sending until crash-recovery burns the whole retry budget.
            // The real destination is never exposed: VaultCrypto exception messages carry only the
            // KeyId, never plaintext.
            _logger.LogError(ex,
                "Failed to decrypt destination for delivery {DeliveryId} (KeyId={KeyId}) — fail-closed",
                delivery.Id, delivery.DestinationKeyId);
            return null;
        }
    }
}
