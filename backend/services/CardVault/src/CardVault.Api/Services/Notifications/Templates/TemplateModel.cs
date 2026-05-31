namespace CardVault.Api.Services.Notifications.Templates;

/// <summary>
/// PCI-safe model passed to Razor notification templates.
/// <para>
/// NEVER include raw PAN, full card number, OTP seed/secret, or any other CHD here.
/// The <see cref="PciTemplateGuard"/> enforces this at pre-render time.
/// </para>
/// </summary>
/// <param name="TemplateType">Template type key (e.g. "Otp", "TransactionNotification").</param>
/// <param name="Locale">BCP-47 locale code (e.g. "es-EC", "en-US"). Null falls back to "es-EC".</param>
/// <param name="MaskedPan">Masked PAN — ONLY the last-4 format (e.g. "****1234"). Never the full PAN.</param>
/// <param name="Amount">Transaction or payment amount. Safe to display.</param>
/// <param name="CurrencyCode">ISO-4217 currency code (e.g. "USD").</param>
/// <param name="MaskedMerchant">Partially masked merchant name (e.g. "AMAZ***").</param>
/// <param name="Timestamp">Event timestamp.</param>
/// <param name="OtpCode">
/// Displayable OTP code for the end-user (e.g. "654321").
/// This is the DISPLAY code only — never the seed or secret.
/// </param>
/// <param name="AdditionalData">
/// Free-form supplementary text. Must NOT contain PAN, account numbers, or OTP secrets.
/// The <see cref="PciTemplateGuard"/> scans this field.
/// </param>
public sealed record TemplateModel(
    string TemplateType,
    string Locale,
    string? MaskedPan,
    decimal? Amount,
    string? CurrencyCode,
    string? MaskedMerchant,
    DateTimeOffset? Timestamp,
    string? OtpCode,
    string? AdditionalData);

/// <summary>
/// The rendered output from <see cref="INotificationTemplateRenderer.RenderAsync"/>.
/// </summary>
/// <param name="Subject">Email subject line or SMS sender label.</param>
/// <param name="Body">Rendered HTML/text body. MUST NOT be logged.</param>
public sealed record RenderedTemplate(string Subject, string Body);
