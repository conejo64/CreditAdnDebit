namespace CardVault.Infrastructure.Notifications.Providers;

/// <summary>
/// Non-secret configuration for the Movistar Ecuador SMS provider.
/// Bound from <c>Notifications:Providers:MovistarEc</c> in appsettings.json.
/// <para>
/// SECURITY: <c>ApiKey</c> is a secret and is intentionally NOT a property here.
/// It is resolved at runtime from the environment variable
/// <c>Notifications__Providers__MovistarEc__ApiKey</c> or user-secrets.
/// </para>
/// </summary>
public sealed class MovistarOptions
{
    /// <summary>
    /// Sender ID shown to the SMS recipient (e.g. <c>CardVault</c>).
    /// Must comply with Movistar EC B2B contract constraints (max 11 chars, alphanumeric).
    /// </summary>
    public string SenderId { get; set; } = "CardVault";

    /// <summary>
    /// When <c>false</c> (default), sends via the Movistar EC SOAP/XML API.
    /// When <c>true</c>, sends via the Movistar EC REST/JSON API.
    /// Controlled at the account/contract level — both protocols reach the same gateway.
    /// </summary>
    public bool UseRestProtocol { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, the provider treats an <c>Accepted</c> send result as immediate delivery
    /// confirmation and sets <c>ProviderReportedAt</c> to the current UTC time.
    /// <para>
    /// Enable when the Movistar EC contract does NOT include a DLR (Delivery Receipt) callback.
    /// In degraded mode, the dispatcher sets <c>DeliveredOn</c> at send time instead of waiting
    /// for a webhook. This is a known SBS-evidence limitation — logged explicitly.
    /// </para>
    /// </summary>
    public bool DegradedConfirmation { get; set; } = false;

    /// <summary>Relative URL path for the SOAP endpoint (default: <c>/ws/sms/v1</c>).</summary>
    public string SoapEndpointPath { get; set; } = "/ws/sms/v1";

    /// <summary>Relative URL path for the REST endpoint (default: <c>/api/v1/sms/send</c>).</summary>
    public string RestEndpointPath { get; set; } = "/api/v1/sms/send";

    /// <summary>XML namespace used in the Movistar EC SOAP envelope body.</summary>
    public string SoapNamespace { get; set; } = "http://sms.movistar.ec/gateway/v1";

    /// <summary>
    /// Base URL of the Movistar EC SMS gateway (default: production endpoint).
    /// Override for staging/sandbox environments.
    /// </summary>
    public string BaseUrl { get; set; } = "https://sms.movistar.ec";
}
