using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;

namespace CardVault.Tests.Features.Notifications.Fakes;

/// <summary>
/// Test double for <see cref="INotificationProvider"/>.
/// <para>
/// Behavior:
/// <list type="bullet">
///   <item>ProviderId = "fake"</item>
///   <item>Returns outcomes from a configurable queue (dequeues per call)</item>
///   <item>Falls back to <see cref="ProviderOutcome.Accepted"/> when queue is empty</item>
///   <item>Records every call in <see cref="Calls"/> for assertion</item>
///   <item><see cref="CanHandle"/> always returns <c>true</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class FakeNotificationProvider : INotificationProvider
{
    private readonly Queue<ProviderOutcome> _outcomeQueue;
    private readonly List<NotificationSendRequest> _calls = new();

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public NotificationChannel Channel { get; }

    /// <summary>All recorded send requests in call order.</summary>
    public IReadOnlyList<NotificationSendRequest> Calls => _calls;

    /// <summary>
    /// Creates a fake provider with ProviderId="fake" for the given channel.
    /// </summary>
    /// <param name="channel">Channel this provider handles.</param>
    /// <param name="outcomes">
    /// Ordered outcomes to return (one per <see cref="SendAsync"/> call).
    /// When exhausted, falls back to <see cref="ProviderOutcome.Accepted"/>.
    /// </param>
    public FakeNotificationProvider(NotificationChannel channel, params ProviderOutcome[] outcomes)
        : this(channel, "fake", outcomes) { }

    /// <summary>
    /// Creates a named fake provider for fallback chain tests.
    /// </summary>
    /// <param name="channel">Channel this provider handles.</param>
    /// <param name="providerId">Provider identifier returned by <see cref="ProviderId"/>.</param>
    /// <param name="outcomes">
    /// Ordered outcomes to return (one per <see cref="SendAsync"/> call).
    /// When exhausted, falls back to <see cref="ProviderOutcome.Accepted"/>.
    /// </param>
    public FakeNotificationProvider(NotificationChannel channel, string providerId, params ProviderOutcome[] outcomes)
    {
        Channel = channel;
        ProviderId = providerId;
        _outcomeQueue = new Queue<ProviderOutcome>(outcomes);
    }

    /// <inheritdoc />
    public bool CanHandle(string destinationE164OrEmail) => true;

    /// <inheritdoc />
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
    {
        _calls.Add(request);

        var outcome = _outcomeQueue.Count > 0
            ? _outcomeQueue.Dequeue()
            : ProviderOutcome.Accepted;

        var result = outcome switch
        {
            ProviderOutcome.Accepted => new ProviderSendResult(
                Outcome: ProviderOutcome.Accepted,
                ProviderReference: $"fake-ref-{request.DeliveryId:N}",
                ErrorCode: null,
                ErrorMessage: null,
                ProviderReportedAt: null),

            ProviderOutcome.TransientFailure => new ProviderSendResult(
                Outcome: ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: "fake-transient",
                ErrorMessage: "Simulated transient failure",
                ProviderReportedAt: null),

            ProviderOutcome.PermanentFailure => new ProviderSendResult(
                Outcome: ProviderOutcome.PermanentFailure,
                ProviderReference: null,
                ErrorCode: "fake-permanent",
                ErrorMessage: "Simulated permanent failure",
                ProviderReportedAt: null),

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };

        return Task.FromResult(result);
    }
}
