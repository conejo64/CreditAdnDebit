using CardVault.Infrastructure.Persistence.Notifications;

namespace CardVault.Api.Services.Notifications;

/// <summary>
/// Thrown when a state-machine transition is illegal for the current delivery status.
/// NOT persisted — logged only. Carries enough context for diagnostics.
/// </summary>
public sealed class InvalidDeliveryTransitionException : Exception
{
    /// <summary>The delivery row this transition was attempted on.</summary>
    public Guid DeliveryId { get; }

    /// <summary>The current (source) status of the delivery.</summary>
    public NotificationDeliveryStatus From { get; }

    /// <summary>The target status that was rejected.</summary>
    public NotificationDeliveryStatus To { get; }

    /// <summary>The caller that attempted the illegal transition.</summary>
    public string Caller { get; }

    /// <summary>
    /// Initializes a new <see cref="InvalidDeliveryTransitionException"/>.
    /// </summary>
    public InvalidDeliveryTransitionException(
        Guid deliveryId,
        NotificationDeliveryStatus from,
        NotificationDeliveryStatus to,
        string caller)
        : base($"Illegal delivery transition '{from}' → '{to}' rejected for delivery {deliveryId} (caller: {caller}).")
    {
        DeliveryId = deliveryId;
        From = from;
        To = to;
        Caller = caller;
    }
}
