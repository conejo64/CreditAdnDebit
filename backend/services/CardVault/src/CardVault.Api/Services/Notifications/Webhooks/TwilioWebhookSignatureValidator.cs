using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CardVault.Api.Services.Notifications.Webhooks;

/// <summary>
/// Validates inbound Twilio webhook requests using HMAC-SHA1.
/// <para>
/// Algorithm: <c>Base64(HMAC-SHA1(AuthToken, url + sortedParamKvPairs))</c>,
/// carried in the <c>X-Twilio-Signature</c> header.
/// Replay guard: <c>X-Twilio-Timestamp</c> must be within 5 minutes of now.
/// All comparisons are constant-time.
/// </para>
/// </summary>
public sealed class TwilioWebhookSignatureValidator : IWebhookSignatureValidator
{
    private readonly TwilioWebhookOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    /// <inheritdoc />
    public string ProviderId => "twilio";

    /// <summary>
    /// Constructs the validator.
    /// </summary>
    /// <param name="options">Twilio webhook options (AuthToken, WebhookUrl).</param>
    /// <param name="clock">Clock factory for testable replay detection. Defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public TwilioWebhookSignatureValidator(
        IOptions<TwilioWebhookOptions> options,
        Func<DateTimeOffset>? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool Validate(HttpRequest request, ReadOnlySpan<byte> rawBody)
    {
        // 1. Missing signature or timestamp → reject immediately
        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var sigHeader) ||
            string.IsNullOrEmpty(sigHeader))
            return false;

        if (!request.Headers.TryGetValue("X-Twilio-Timestamp", out var tsHeader) ||
            !long.TryParse(tsHeader, out var epochSeconds))
            return false;

        // 2. Replay guard: reject if timestamp is 5 minutes or older
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        if (!WebhookValidatorHelper.IsWithinReplayWindow(timestamp, _clock()))
            return false;

        // 3. Compute expected signature — fail closed if form reading throws
        string paramString;
        try
        {
            paramString = BuildSortedParamString(request);
        }
        catch (InvalidOperationException)
        {
            // WARN-3: form collection access failed (e.g. body not yet read or content-type
            // mismatch). Do not swallow silently in a security path — fail closed.
            return false;
        }
        var data = Encoding.UTF8.GetBytes(_options.WebhookUrl + paramString);
        var key = Encoding.UTF8.GetBytes(_options.AuthToken);
        var expectedHmac = HMACSHA1.HashData(key, data);
        var expectedSig = Convert.ToBase64String(expectedHmac);

        // 4. Constant-time comparison
        var actualBytes = Encoding.UTF8.GetBytes(sigHeader.ToString());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSig);

        if (actualBytes.Length != expectedBytes.Length)
        {
            // WARN-2: dummy comparison must use expectedBytes (not actualBytes) in both
            // arguments so timing is independent of the attacker-supplied input length.
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    /// <summary>
    /// Builds the sorted parameter string per Twilio's signature algorithm:
    /// concatenate key+value pairs sorted by key (ASCII order), no separator.
    /// </summary>
    private static string BuildSortedParamString(HttpRequest request)
    {
        if (!request.HasFormContentType)
            return string.Empty;

        // Read form values synchronously — IFormCollection is already loaded at this point
        // (middleware must have read the body before calling this validator).
        // WARN-3: do not catch broadly here; let InvalidOperationException propagate to
        // the caller which handles it as a closed failure (return false in Validate).
        var form = request.Form;
        if (form.Count == 0)
            return string.Empty;

        // Per Twilio docs: for multi-value params, values must be sorted alphabetically
        // and concatenated (not comma-separated). StringValues.ToString() uses insertion
        // order — we must sort explicitly to match the documented signing algorithm.
        return string.Concat(
            form
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + string.Concat(kv.Value.OrderBy(v => v, StringComparer.Ordinal))));
    }
}
