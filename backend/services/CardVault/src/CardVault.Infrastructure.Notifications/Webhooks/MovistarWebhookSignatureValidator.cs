using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CardVault.Infrastructure.Notifications.Webhooks;

/// <summary>
/// Validates inbound Movistar Ecuador webhook requests using HMAC-SHA256.
/// <para>
/// Algorithm: <c>HMAC-SHA256(WebhookSecret, rawBody)</c>, hex-encoded (lower or upper case),
/// carried in the <c>X-Movistar-Signature</c> header.
/// Replay guard: <c>X-Movistar-Timestamp</c> (Unix epoch seconds) must be within 5 minutes.
/// All comparisons are constant-time.
/// </para>
/// </summary>
public sealed class MovistarWebhookSignatureValidator : IWebhookSignatureValidator
{
    private readonly MovistarWebhookOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    /// <inheritdoc />
    public string ProviderId => "movistar-ec";

    /// <inheritdoc />
    public string SignatureHeaderName => "X-Movistar-Signature";

    /// <summary>
    /// Constructs the validator.
    /// </summary>
    /// <param name="options">Movistar webhook options (WebhookSecret).</param>
    /// <param name="clock">Clock factory for testable replay detection. Defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public MovistarWebhookSignatureValidator(
        IOptions<MovistarWebhookOptions> options,
        Func<DateTimeOffset>? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public WebhookValidationResult Validate(HttpRequest request, ReadOnlySpan<byte> rawBody)
    {
        // 1. Missing signature → missing-signature
        if (!request.Headers.TryGetValue("X-Movistar-Signature", out var sigHeader) ||
            string.IsNullOrEmpty(sigHeader))
            return WebhookValidationResult.MissingSignature;

        // 2. Replay guard via timestamp header
        if (!request.Headers.TryGetValue("X-Movistar-Timestamp", out var tsHeader) ||
            !long.TryParse(tsHeader, out var epochSeconds))
            return WebhookValidationResult.MissingSignature;

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        if (!WebhookValidatorHelper.IsWithinReplayWindow(timestamp, _clock()))
            return WebhookValidationResult.Replayed;

        // 3. Compute expected HMAC-SHA256 over the raw body
        var key = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var expectedHmac = HMACSHA256.HashData(key, rawBody);
        var expectedHex = Convert.ToHexString(expectedHmac); // returns uppercase

        // 4. Normalize incoming signature to uppercase for comparison
        var actualHex = sigHeader.ToString().ToUpperInvariant();
        var expectedHexUpper = expectedHex; // already uppercase from Convert.ToHexString

        // 5. Constant-time comparison (compare as UTF-8 bytes)
        var actualBytes = Encoding.ASCII.GetBytes(actualHex);
        var expectedBytes = Encoding.ASCII.GetBytes(expectedHexUpper);

        if (actualBytes.Length != expectedBytes.Length)
        {
            // WARN-2: dummy comparison uses expectedBytes in both args so timing is
            // independent of the attacker-supplied input length (not actualBytes).
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return WebhookValidationResult.InvalidSignature;
        }

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes)
            ? WebhookValidationResult.Valid
            : WebhookValidationResult.InvalidSignature;
    }
}
