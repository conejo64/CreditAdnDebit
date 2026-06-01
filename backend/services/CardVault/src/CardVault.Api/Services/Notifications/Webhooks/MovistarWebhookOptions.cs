namespace CardVault.Api.Services.Notifications.Webhooks;

/// <summary>
/// Secret configuration for the Movistar EC webhook signature validator.
/// <para>
/// SECURITY: <c>WebhookSecret</c> MUST be read from environment variable
/// <c>Notifications__Providers__MovistarEc__WebhookSecret</c> — NOT from appsettings.json.
/// </para>
/// </summary>
public sealed class MovistarWebhookOptions
{
    /// <summary>
    /// Shared secret negotiated with Movistar Ecuador, used as the HMAC-SHA256 key.
    /// Read at runtime from <c>Notifications__Providers__MovistarEc__WebhookSecret</c>.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
