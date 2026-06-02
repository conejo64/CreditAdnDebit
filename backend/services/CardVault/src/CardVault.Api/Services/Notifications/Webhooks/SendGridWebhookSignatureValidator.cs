using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CardVault.Api.Services.Notifications.Webhooks;

/// <summary>
/// Validates inbound SendGrid Event Webhook requests using ECDSA (P-256 / SHA-256).
/// <para>
/// Algorithm: verify <c>ECDSA-P256-SHA256(publicKey, timestamp + rawBody)</c>.
/// Signature carried in <c>X-Twilio-Email-Event-Webhook-Signature</c> (Base64 DER).
/// Timestamp carried in <c>X-Twilio-Email-Event-Webhook-Timestamp</c> (Unix epoch seconds).
/// Replay guard: timestamp must be within 5 minutes of now.
/// </para>
/// <para>
/// NOTE: Despite the "X-Twilio-Email" prefix, this is a SendGrid header (Twilio owns SendGrid).
/// The scheme is ECDSA — NOT HMAC. The design doc explicitly corrects the spec's "HMAC" wording.
/// </para>
/// </summary>
public sealed class SendGridWebhookSignatureValidator : IWebhookSignatureValidator
{
    private readonly Func<DateTimeOffset> _clock;

    /// <inheritdoc />
    public string ProviderId => "sendgrid";

    private const string SignatureHeader = "X-Twilio-Email-Event-Webhook-Signature";
    private const string TimestampHeader = "X-Twilio-Email-Event-Webhook-Timestamp";

    // WARN-4: cache the parsed ECParameters once at construction time.
    // IOptions<T> is immutable after startup — no need to re-parse the PEM on every request.
    // ECDsa instances are not safe for concurrent VerifyData calls, so we store ECParameters
    // and create a cheap per-call ECDsa from those (struct copy, no PEM/DER parsing).
    private readonly ECParameters _publicKeyParameters;

    /// <summary>
    /// Constructs the validator, parsing and caching the public key from the options PEM.
    /// </summary>
    /// <param name="options">SendGrid webhook options (PEM public key).</param>
    /// <param name="clock">Clock factory for testable replay detection. Defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public SendGridWebhookSignatureValidator(
        IOptions<SendGridWebhookOptions> options,
        Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        // Parse PEM once at construction. Throws CryptographicException if the PEM is invalid,
        // which is intentional — fail fast at startup rather than silently at request time.
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(ImportPemPublicKey(options.Value.WebhookPublicKeyPem), out _);
        _publicKeyParameters = ecdsa.ExportParameters(includePrivateParameters: false);
    }

    /// <inheritdoc />
    public bool Validate(HttpRequest request, ReadOnlySpan<byte> rawBody)
    {
        // 1. Missing headers → reject
        if (!request.Headers.TryGetValue(SignatureHeader, out var sigHeader) ||
            string.IsNullOrEmpty(sigHeader))
            return false;

        if (!request.Headers.TryGetValue(TimestampHeader, out var tsHeader) ||
            string.IsNullOrEmpty(tsHeader))
            return false;

        var timestampStr = tsHeader.ToString();

        // 2. Replay guard
        if (!long.TryParse(timestampStr, out var epochSeconds))
            return false;

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        if (!WebhookValidatorHelper.IsWithinReplayWindow(timestamp, _clock()))
            return false;

        // 3. Decode the base64 signature
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(sigHeader.ToString());
        }
        catch (FormatException)
        {
            return false;
        }

        // 4. Build the payload: UTF8(timestamp) + rawBody
        var timestampBytes = Encoding.UTF8.GetBytes(timestampStr);
        var payload = new byte[timestampBytes.Length + rawBody.Length];
        timestampBytes.CopyTo(payload, 0);
        rawBody.CopyTo(payload.AsSpan(timestampBytes.Length));

        // 5. Verify ECDSA signature using the cached public key parameters.
        // A fresh ECDsa instance is created from the cached ECParameters (struct copy,
        // no PEM/DER work) so concurrent requests each get their own verifier.
        try
        {
            using var ecdsa = ECDsa.Create(_publicKeyParameters);
            return ecdsa.VerifyData(
                payload,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Strips PEM armor and returns the raw DER bytes for the SubjectPublicKeyInfo structure.
    /// Used once at construction time; result is passed to <see cref="ECDsa.ImportSubjectPublicKeyInfo"/>.
    /// </summary>
    private static ReadOnlySpan<byte> ImportPemPublicKey(string pem)
    {
        // Strip PEM headers manually to extract the base64 DER body,
        // then pass the decoded bytes to ImportSubjectPublicKeyInfo.
        const string begin = "-----BEGIN PUBLIC KEY-----";
        const string end = "-----END PUBLIC KEY-----";

        var start = pem.IndexOf(begin, StringComparison.Ordinal);
        var finish = pem.IndexOf(end, StringComparison.Ordinal);
        if (start < 0 || finish < 0)
            throw new CryptographicException("Invalid PEM format: missing BEGIN/END markers.");

        var base64 = pem[(start + begin.Length)..finish]
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        return Convert.FromBase64String(base64);
    }
}
