namespace CardVault.Api.Services.Notifications.Webhooks;

/// <summary>
/// Internal helpers shared across webhook signature validators.
/// </summary>
internal static class WebhookValidatorHelper
{
    private const int ReplayWindowSeconds = 300; // 5 minutes

    /// <summary>
    /// Returns <c>true</c> when the request is within the allowed replay window.
    /// A timestamp at exactly 5 minutes is considered stale (uses strict inequality).
    /// </summary>
    /// <param name="requestTimestamp">Timestamp extracted from the webhook request header.</param>
    /// <param name="now">Current time (injected for testability).</param>
    internal static bool IsWithinReplayWindow(DateTimeOffset requestTimestamp, DateTimeOffset now)
    {
        var ageSeconds = (now - requestTimestamp).TotalSeconds;
        return ageSeconds < ReplayWindowSeconds;
    }
}
