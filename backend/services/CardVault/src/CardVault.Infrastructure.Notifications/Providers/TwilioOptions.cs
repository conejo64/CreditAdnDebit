namespace CardVault.Infrastructure.Notifications.Providers;

/// <summary>
/// Non-secret configuration for the Twilio SMS provider.
/// Bound from <c>Notifications:Providers:Twilio</c> in appsettings.json.
/// <para>
/// SECURITY: <c>AuthToken</c> is a secret and is intentionally NOT a property here.
/// It is resolved at runtime from the environment variable
/// <c>Notifications__Providers__Twilio__AuthToken</c> or user-secrets.
/// </para>
/// </summary>
public sealed class TwilioOptions
{
    /// <summary>Twilio Account SID (non-secret — identifies the account, not the credential).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio sender phone number in E.164 format (e.g. <c>+15550001234</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL for Twilio to call with delivery status updates.
    /// Must be a publicly reachable HTTPS URL.
    /// </summary>
    public string? StatusCallbackUrl { get; set; }
}
