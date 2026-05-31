using CardVault.Infrastructure.Persistence.Notifications;

namespace CardVault.Api.Services.Notifications;

/// <summary>
/// Stateless 5-state delivery FSM.
/// Accepts a <see cref="Func{DateTimeOffset}"/> clock for testability.
/// Default clock: <c>() => DateTimeOffset.UtcNow</c>.
/// </summary>
public sealed class DeliveryStateMachine : IDeliveryStateMachine
{
    private readonly Func<DateTimeOffset> _clock;

    // Backoff base durations
    private static readonly TimeSpan Attempt1Base = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Attempt2Base = TimeSpan.FromMinutes(2);

    // Jitter factor: ±10%
    private const double JitterFactor = 0.10;

    /// <summary>
    /// Legal transition table. Sent and DeadLetter are terminal states for Status.
    /// (DeliveredOn may still be updated on a Sent row by a webhook — that is a field update,
    /// NOT a status transition.)
    /// </summary>
    private static readonly HashSet<(NotificationDeliveryStatus From, NotificationDeliveryStatus To)> LegalTransitions
        = new()
        {
            (NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.Sending),
            (NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.Sent),
            (NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.Failed),
            (NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.DeadLetter),
            (NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.Sending),
            (NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.DeadLetter),
        };

    /// <summary>
    /// Creates a new <see cref="DeliveryStateMachine"/>.
    /// </summary>
    /// <param name="clock">
    /// Optional clock factory. Defaults to <c>() => DateTimeOffset.UtcNow</c>.
    /// Inject a fixed value in tests for deterministic results.
    /// </param>
    public DeliveryStateMachine(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool CanTransition(NotificationDeliveryStatus from, NotificationDeliveryStatus to)
        => LegalTransitions.Contains((from, to));

    /// <inheritdoc />
    public void Transition(CustomerNotificationDeliveryEntity d, NotificationDeliveryStatus to)
    {
        if (!CanTransition(d.Status, to))
            throw new InvalidDeliveryTransitionException(d.Id, d.Status, to, nameof(DeliveryStateMachine));

        var now = _clock();

        // Set SendingStartedOn when entering Sending state
        if (to == NotificationDeliveryStatus.Sending)
        {
            d.SendingStartedOn = now;
        }

        // Clear SendingStartedOn when leaving Sending state (terminal or failure)
        if (to is NotificationDeliveryStatus.Sent
               or NotificationDeliveryStatus.Failed
               or NotificationDeliveryStatus.DeadLetter)
        {
            d.SendingStartedOn = null;
        }

        d.Status = to;
    }

    /// <inheritdoc />
    public DateTimeOffset ComputeNextAttempt(int attempts, DateTimeOffset now)
    {
        var baseDelay = attempts switch
        {
            1 => Attempt1Base,
            _ => Attempt2Base   // attempt 2+ uses 2-minute base
        };

        var jitter = (Random.Shared.NextDouble() * 2 - 1) * JitterFactor; // [-0.10, +0.10]
        var jitteredSeconds = baseDelay.TotalSeconds * (1 + jitter);

        return now.AddSeconds(jitteredSeconds);
    }
}
