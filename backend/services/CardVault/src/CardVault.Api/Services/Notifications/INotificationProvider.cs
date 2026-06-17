using CardVault.Infrastructure.Persistence.Notifications;
using Microsoft.AspNetCore.Http;

namespace CardVault.Application.Services.Notifications;

/// <summary>
/// Outcome returned by a provider after a send attempt.
/// </summary>
public enum ProviderOutcome
{
    /// <summary>Provider accepted the message for delivery.</summary>
    Accepted,

    /// <summary>Transient failure — eligible for retry.</summary>
    TransientFailure,

    /// <summary>Permanent failure — do NOT retry; route to DeadLetter.</summary>
    PermanentFailure
}

/// <summary>
/// Immutable result returned by <see cref="INotificationProvider.SendAsync"/>.
/// </summary>
public sealed record ProviderSendResult(
    ProviderOutcome Outcome,
    string? ProviderReference,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? ProviderReportedAt);

/// <summary>
/// Input to a provider send call. Contains the UNMASKED destination —
/// must NEVER be logged or audited outside the provider adapter.
/// </summary>
public sealed record NotificationSendRequest(
    Guid DeliveryId,
    Guid TenantId,
    NotificationChannel Channel,
    string Destination,
    string RenderedSubject,
    string RenderedBody,
    string TemplateType,
    string Locale);

/// <summary>
/// Strategy adapter for a single notification provider (Twilio, SendGrid, Movistar-EC).
/// Each adapter fully encapsulates its wire protocol.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Stable provider identifier (e.g. "twilio", "sendgrid", "movistar-ec").</summary>
    string ProviderId { get; }

    /// <summary>The channel this provider handles.</summary>
    NotificationChannel Channel { get; }

    /// <summary>
    /// Returns <c>true</c> if this provider can handle the given destination.
    /// Movistar-EC restricts to <c>+593</c> prefix; others return <c>true</c> unconditionally.
    /// </summary>
    bool CanHandle(string destinationE164OrEmail);

    /// <summary>
    /// Sends the notification and returns a classified result.
    /// The adapter is responsible for classifying transient vs permanent failures.
    /// </summary>
    Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct);
}

/// <summary>
/// Resolves an ordered provider chain for a given (tenantId, channel, destination).
/// Slice 1: returns fixed chains [Twilio] for SMS, [SendGrid] for Email.
/// Slice 2: adds DB-backed per-tenant routing.
/// </summary>
public interface INotificationProviderRegistry
{
    /// <summary>
    /// Returns the ordered list of providers to attempt for this delivery.
    /// Filtered by <see cref="INotificationProvider.CanHandle"/>.
    /// </summary>
    IReadOnlyList<INotificationProvider> ResolveChain(Guid tenantId, NotificationChannel channel, string destination);
}

/// <summary>
/// Claims a batch of pending/failed deliveries, drives the FSM, calls providers, persists transitions.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Claims up to <paramref name="take"/> rows, dispatches them, and returns
    /// the count of rows that reached a terminal state (Sent or DeadLetter) this tick.
    /// </summary>
    Task<int> DispatchBatchAsync(int take, CancellationToken ct);
}

/// <summary>
/// Guards delivery status transitions and computes backoff intervals.
/// Stateless and injectable.
/// </summary>
public interface IDeliveryStateMachine
{
    /// <summary>Returns <c>true</c> if the transition is legal per the 5-state FSM.</summary>
    bool CanTransition(NotificationDeliveryStatus from, NotificationDeliveryStatus to);

    /// <summary>
    /// Applies a valid transition to the entity, updating timestamps and status.
    /// Throws <see cref="InvalidDeliveryTransitionException"/> on illegal transitions.
    /// </summary>
    void Transition(CustomerNotificationDeliveryEntity d, NotificationDeliveryStatus to);

    /// <summary>
    /// Computes the earliest next attempt time with ±10% jitter.
    /// <list type="bullet">
    ///   <item>attempt 1 → now + 30 s ± 10%</item>
    ///   <item>attempt 2 → now + 2 min ± 10%</item>
    /// </list>
    /// </summary>
    DateTimeOffset ComputeNextAttempt(int attempts, DateTimeOffset now);
}

/// <summary>
/// Discriminated result returned by <see cref="IWebhookSignatureValidator.Validate"/>.
/// Each variant maps to a distinct audit reason in the controller so SIEM rules can
/// distinguish replay attacks from signature tampering.
/// </summary>
public enum WebhookValidationResult
{
    /// <summary>Signature is present, valid, and within the replay window.</summary>
    Valid,

    /// <summary>
    /// The expected signature header is absent from the request.
    /// Controller maps to HTTP 401 + audit reason "missing-signature".
    /// </summary>
    MissingSignature,

    /// <summary>
    /// The signature header is present but the computed HMAC/ECDSA does not match.
    /// Controller maps to HTTP 401 + audit reason "invalid-signature".
    /// </summary>
    InvalidSignature,

    /// <summary>
    /// The signature is cryptographically valid but the timestamp falls outside the
    /// 5-minute replay window (too old or future-dated).
    /// Controller maps to HTTP 401 + audit reason "replayed".
    /// </summary>
    Replayed
}

/// <summary>
/// Validates an inbound webhook request signature for a specific provider.
/// </summary>
public interface IWebhookSignatureValidator
{
    /// <summary>Stable provider identifier this validator handles.</summary>
    string ProviderId { get; }

    /// <summary>
    /// The name of the HTTP header that carries the signature for this provider.
    /// The endpoint controller reads this to distinguish a "missing signature" failure
    /// (header absent → <c>missing-signature</c> audit reason) from a "tampered" failure
    /// (header present but validation fails → <c>invalid-signature</c> audit reason).
    /// </summary>
    string SignatureHeaderName { get; }

    /// <summary>
    /// Returns a <see cref="WebhookValidationResult"/> indicating whether the request
    /// carries a valid, non-replayed signature or the specific failure reason.
    /// Implementations must use constant-time comparison for HMAC/ECDSA verification.
    /// </summary>
    WebhookValidationResult Validate(HttpRequest request, ReadOnlySpan<byte> rawBody);
}
