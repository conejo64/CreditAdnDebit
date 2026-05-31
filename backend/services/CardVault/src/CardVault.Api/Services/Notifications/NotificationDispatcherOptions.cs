namespace CardVault.Api.Services.Notifications;

/// <summary>
/// Configuration options for the notification dispatcher.
/// Bound from <c>Notifications:Dispatcher</c> in appsettings.json.
/// Secrets (API keys, auth tokens) are NOT stored here — use env vars or user-secrets.
/// </summary>
public sealed class NotificationDispatcherOptions
{
    /// <summary>
    /// When <c>false</c> (the default and safe default), the dispatcher leaves pending
    /// rows as-is and does NOT attempt to send via real providers.
    /// Set to <c>true</c> only in production-equivalent environments with real secrets configured.
    /// </summary>
    public bool RealProvidersEnabled { get; set; } = false;

    /// <summary>
    /// Maximum number of attempts across the entire provider chain (shared budget).
    /// Default: 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Lock TTL in minutes. Sending rows older than this are reclaimed for crash recovery.
    /// Default: 5 minutes.
    /// </summary>
    public int LockTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of deliveries to claim per dispatcher tick.
    /// Default: 50.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
