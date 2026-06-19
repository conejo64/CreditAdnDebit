namespace CardVault.Infrastructure.Notifications.Webhooks;

/// <summary>
/// Secret configuration for the Twilio webhook signature validator.
/// <para>
/// SECURITY: <c>AuthToken</c> MUST be read from environment variable
/// <c>Notifications__Providers__Twilio__AuthToken</c> — NOT from appsettings.json.
/// </para>
/// </summary>
public sealed class TwilioWebhookOptions
{
    /// <summary>
    /// Twilio Auth Token used as the HMAC-SHA1 key.
    /// Read at runtime from <c>Notifications__Providers__Twilio__AuthToken</c>.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// The fully-qualified HTTPS URL that Twilio will POST to.
    /// Must match exactly (scheme, host, path) to compute the expected signature.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;
}
