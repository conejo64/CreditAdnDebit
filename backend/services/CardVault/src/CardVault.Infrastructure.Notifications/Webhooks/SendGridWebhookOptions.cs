namespace CardVault.Infrastructure.Notifications.Webhooks;

/// <summary>
/// Secret configuration for the SendGrid webhook signature validator.
/// <para>
/// SECURITY: <c>WebhookPublicKeyPem</c> MUST be read from environment variable
/// <c>Notifications__Providers__SendGrid__WebhookPublicKey</c> — NOT from appsettings.json.
/// The public key is not a secret per se, but is infrastructure configuration that
/// belongs in the environment, not source code.
/// </para>
/// </summary>
public sealed class SendGridWebhookOptions
{
    /// <summary>
    /// PEM-encoded ECDSA P-256 public key provided by SendGrid in the Event Webhook settings.
    /// Used to verify <c>X-Twilio-Email-Event-Webhook-Signature</c> headers.
    /// </summary>
    public string WebhookPublicKeyPem { get; set; } = string.Empty;
}
