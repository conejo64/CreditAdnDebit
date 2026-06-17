namespace CardVault.Application.Services.Notifications.Providers;

/// <summary>
/// Non-secret configuration for the SendGrid email provider.
/// Bound from <c>Notifications:Providers:SendGrid</c> in appsettings.json.
/// <para>
/// SECURITY: <c>ApiKey</c> is a secret and is intentionally NOT a property here.
/// It is resolved at runtime from the environment variable
/// <c>Notifications__Providers__SendGrid__ApiKey</c> or user-secrets.
/// </para>
/// </summary>
public sealed class SendGridOptions
{
    /// <summary>Sender email address (e.g. <c>noreply@example.com</c>).</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Display name for the sender (e.g. <c>CardVault</c>).</summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Map of template type to SendGrid Dynamic Template ID.
    /// Key: template type (e.g. <c>Otp</c>, <c>TransactionNotification</c>).
    /// Value: SendGrid template ID (e.g. <c>d-abc123</c>).
    /// When empty, the adapter sends a plain-text email using the rendered body.
    /// </summary>
    public Dictionary<string, string> TemplateIdMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
