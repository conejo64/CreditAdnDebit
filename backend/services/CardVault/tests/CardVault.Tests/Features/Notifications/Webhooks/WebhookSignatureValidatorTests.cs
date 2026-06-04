using System.Security.Cryptography;
using System.Text;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Webhooks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Features.Notifications.Webhooks;

/// <summary>
/// Strict-TDD RED-phase tests for <see cref="IWebhookSignatureValidator"/> implementations.
/// Uses pre-computed signatures with known secrets — NEVER calls real provider endpoints.
/// All validators must use constant-time comparison (CryptographicOperations.FixedTimeEquals).
/// </summary>
public sealed class WebhookSignatureValidatorTests
{
    // ── Shared constants ──────────────────────────────────────────────────────

    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecentTimestamp = FrozenNow.AddMinutes(-2);
    private static readonly DateTimeOffset StaleTimestamp = FrozenNow.AddMinutes(-6);

    // ═══════════════════════════════════════════════════════════════════════════
    // §1 — Twilio webhook validator (HMAC-SHA1)
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class TwilioValidatorTests
    {
        private const string AuthToken = "test-twilio-auth-token-32charslng!";
        private const string WebhookUrl = "https://example.com/api/notifications/delivery-callback/twilio";

        // ── Helpers ──────────────────────────────────────────────────────────

        private static TwilioWebhookSignatureValidator CreateValidator(DateTimeOffset? now = null)
        {
            var options = Options.Create(new TwilioWebhookOptions
            {
                AuthToken = AuthToken,
                WebhookUrl = WebhookUrl
            });
            return new TwilioWebhookSignatureValidator(options, () => now ?? FrozenNow);
        }

        /// <summary>
        /// Computes expected Twilio signature: HMAC-SHA1(AuthToken, url + sortedParams).
        /// </summary>
        private static string ComputeExpectedSignature(
            string authToken,
            string url,
            IReadOnlyDictionary<string, string>? formParams = null)
        {
            var paramString = string.Empty;
            if (formParams is { Count: > 0 })
            {
                paramString = string.Concat(
                    formParams
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => kv.Key + kv.Value));
            }

            var data = Encoding.UTF8.GetBytes(url + paramString);
            var key = Encoding.UTF8.GetBytes(authToken);
            var hmac = HMACSHA1.HashData(key, data);
            return Convert.ToBase64String(hmac);
        }

        private static HttpRequest BuildRequest(
            string signature,
            DateTimeOffset timestamp,
            IReadOnlyDictionary<string, string>? formParams = null)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Method = "POST";
            request.Scheme = "https";
            request.Host = new HostString("example.com");
            request.Path = "/api/notifications/delivery-callback/twilio";

            request.Headers["X-Twilio-Signature"] = signature;
            request.Headers["X-Twilio-Timestamp"] = timestamp.ToUnixTimeSeconds().ToString();

            if (formParams is { Count: > 0 })
            {
                request.ContentType = "application/x-www-form-urlencoded";
                foreach (var kv in formParams)
                    request.Form = new FormCollection(
                        formParams.ToDictionary(p => p.Key, p => new Microsoft.Extensions.Primitives.StringValues(p.Value)));
            }

            return request;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Validate_ValidSignatureNoParams_ReturnsValid()
        {
            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var request = BuildRequest(expected, RecentTimestamp);
            var body = Array.Empty<byte>();

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_ValidSignatureWithSortedParams_ReturnsValid()
        {
            var formParams = new Dictionary<string, string>
            {
                ["MessageStatus"] = "delivered",
                ["AccountSid"] = "ACtest",
                ["MessageSid"] = "SM001"
            };

            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl, formParams);
            var request = BuildRequest(expected, RecentTimestamp, formParams);
            var body = Array.Empty<byte>();

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_MissingSignatureHeader_ReturnsMissingSignature()
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Twilio-Timestamp"] = RecentTimestamp.ToUnixTimeSeconds().ToString();
            // X-Twilio-Signature intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.MissingSignature);
        }

        [Fact]
        public void Validate_TamperedSignature_ReturnsInvalidSignature()
        {
            // Correct signature but then modified one char
            var correct = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var tampered = correct.Length > 0 ? correct[..^1] + (correct[^1] == 'A' ? 'B' : 'A') : "TAMPERED";

            var request = BuildRequest(tampered, RecentTimestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_TamperedBody_ReturnsInvalidSignature()
        {
            // Signature computed over different params than what's in the request
            var signatureParams = new Dictionary<string, string> { ["MessageStatus"] = "delivered" };
            var tamperedParams = new Dictionary<string, string> { ["MessageStatus"] = "undelivered" };

            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl, signatureParams);
            var request = BuildRequest(expected, RecentTimestamp, tamperedParams);

            var validator = CreateValidator();
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_ReplayAttack_TimestampOlderThan5Min_ReturnsReplayed()
        {
            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var request = BuildRequest(expected, StaleTimestamp);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a stale timestamp must return Replayed, not InvalidSignature");
        }

        [Fact]
        public void Validate_TimestampExactly5MinAgo_ReturnsReplayed()
        {
            // Exactly at the boundary — should be rejected (>= 5 min)
            var boundaryTimestamp = FrozenNow.AddMinutes(-5);
            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var request = BuildRequest(expected, boundaryTimestamp);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.Replayed);
        }

        [Fact]
        public void Validate_TimestampJustUnder5MinAgo_ReturnsValid()
        {
            var recentEnough = FrozenNow.AddSeconds(-299); // 4m59s old — within window
            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var request = BuildRequest(expected, recentEnough);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_FutureDatedTimestamp_ReturnsReplayed()
        {
            // CRIT-1 (1e.1): a timestamp 10 minutes in the FUTURE must be rejected.
            // After the fix ageSeconds < 0 is caught by ageSeconds >= 0 check → Replayed.
            var futureTimestamp = FrozenNow.AddMinutes(10);
            var expected = ComputeExpectedSignature(AuthToken, WebhookUrl);
            var request = BuildRequest(expected, futureTimestamp);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a future-dated timestamp must be rejected with Replayed by the replay guard");
        }

        [Fact]
        public void Validate_MissingTimestampHeader_ReturnsMissingSignature()
        {
            // SUGG-2: explicit test for missing timestamp-only path.
            var body = Array.Empty<byte>();
            var sig = ComputeExpectedSignature(AuthToken, WebhookUrl);

            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Twilio-Signature"] = sig;
            // X-Twilio-Timestamp intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.MissingSignature,
                because: "a missing timestamp header must be rejected");
        }

        [Fact]
        public void Validate_MultiValueParamSortedAlphabetically_ReturnsValid()
        {
            // WARN-1: Twilio's documented algorithm for multi-value params:
            // values for a given key must be sorted alphabetically and concatenated.
            // The request arrives with values in insertion order ["zebra", "apple"];
            // the signature must be computed over the sorted order ["apple", "zebra"].
            const string multiKey = "Status";
            // Twilio expects values sorted: apple, zebra → concatenated as "applezebra"
            var paramString = multiKey + "applezebra"; // sorted concat
            var data = Encoding.UTF8.GetBytes(WebhookUrl + paramString);
            var key = Encoding.UTF8.GetBytes(AuthToken);
            var hmac = HMACSHA1.HashData(key, data);
            var expectedSig = Convert.ToBase64String(hmac);

            // Build request with multi-value in insertion order ["zebra", "apple"]
            var context = new DefaultHttpContext();
            var req = context.Request;
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Headers["X-Twilio-Signature"] = expectedSig;
            req.Headers["X-Twilio-Timestamp"] = RecentTimestamp.ToUnixTimeSeconds().ToString();
            req.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                [multiKey] = new Microsoft.Extensions.Primitives.StringValues(new[] { "zebra", "apple" })
            });

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(req, Array.Empty<byte>());

            result.Should().Be(WebhookValidationResult.Valid,
                because: "multi-value params must be sorted alphabetically before HMAC");
        }

        [Fact]
        public void ProviderId_IstwilioLowercase()
        {
            var validator = CreateValidator();
            validator.ProviderId.Should().Be("twilio");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §2 — SendGrid webhook validator (ECDSA P-256)
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class SendGridValidatorTests
    {
        // Generate a test EC key pair (P-256) — deterministic per test run via static ctor
        private static readonly ECDsa _testKey;
        private static readonly string _testPublicKeyPem;

        static SendGridValidatorTests()
        {
            _testKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _testPublicKeyPem = _testKey.ExportSubjectPublicKeyInfoPem();
        }

        private static SendGridWebhookSignatureValidator CreateValidator(DateTimeOffset? now = null)
        {
            var options = Options.Create(new SendGridWebhookOptions
            {
                WebhookPublicKeyPem = _testPublicKeyPem
            });
            return new SendGridWebhookSignatureValidator(options, () => now ?? FrozenNow);
        }

        /// <summary>
        /// Signs <c>timestamp + rawBody</c> with the test private key (ECDSA P-256 / SHA-256).
        /// Returns base64url-encoded DER-encoded signature.
        /// </summary>
        private static string ComputeExpectedSignature(string timestamp, byte[] rawBody)
        {
            var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            var payload = new byte[timestampBytes.Length + rawBody.Length];
            timestampBytes.CopyTo(payload, 0);
            rawBody.CopyTo(payload, timestampBytes.Length);

            var sig = _testKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return Convert.ToBase64String(sig);
        }

        private static HttpRequest BuildRequest(string signature, string timestamp)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Method = "POST";
            request.Scheme = "https";
            request.Host = new HostString("example.com");
            request.Path = "/api/notifications/delivery-callback/sendgrid";

            request.Headers["X-Twilio-Email-Event-Webhook-Signature"] = signature;
            request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"] = timestamp;

            return request;
        }

        private static byte[] MakeBody(string content = "{\"event\":\"delivered\"}") =>
            Encoding.UTF8.GetBytes(content);

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Validate_ValidEcdsaSignature_ReturnsValid()
        {
            var body = MakeBody();
            var timestamp = RecentTimestamp.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(timestamp, body);
            var request = BuildRequest(sig, timestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_InvalidSignature_WrongKey_ReturnsInvalidSignature()
        {
            using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var body = MakeBody();
            var timestamp = RecentTimestamp.ToUnixTimeSeconds().ToString();

            var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            var payload = new byte[timestampBytes.Length + body.Length];
            timestampBytes.CopyTo(payload, 0);
            body.CopyTo(payload, timestampBytes.Length);
            var wrongSig = wrongKey.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var sig = Convert.ToBase64String(wrongSig);

            var request = BuildRequest(sig, timestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_TamperedBody_ReturnsInvalidSignature()
        {
            var originalBody = MakeBody();
            var timestamp = RecentTimestamp.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(timestamp, originalBody);
            var request = BuildRequest(sig, timestamp);

            // Different body passed during validation
            var tamperedBody = MakeBody("{\"event\":\"bounced\"}");

            var validator = CreateValidator();
            var result = validator.Validate(request, tamperedBody);

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_MissingSignatureHeader_ReturnsMissingSignature()
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"] =
                RecentTimestamp.ToUnixTimeSeconds().ToString();
            // Signature header intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, MakeBody());

            result.Should().Be(WebhookValidationResult.MissingSignature);
        }

        [Fact]
        public void Validate_MissingTimestampHeader_ReturnsMissingSignature()
        {
            var body = MakeBody();
            var timestamp = RecentTimestamp.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(timestamp, body);

            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Twilio-Email-Event-Webhook-Signature"] = sig;
            // Timestamp header intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.MissingSignature);
        }

        [Fact]
        public void Validate_ReplayAttack_StaleTimestamp_ReturnsReplayed()
        {
            var body = MakeBody();
            var staleTimestampStr = StaleTimestamp.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(staleTimestampStr, body);
            var request = BuildRequest(sig, staleTimestampStr);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a stale timestamp must return Replayed, not InvalidSignature");
        }

        [Fact]
        public void Validate_TimestampJustUnder5MinAgo_ReturnsValid()
        {
            var body = MakeBody();
            var recentEnough = FrozenNow.AddSeconds(-299);
            var timestampStr = recentEnough.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(timestampStr, body);
            var request = BuildRequest(sig, timestampStr);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_FutureDatedTimestamp_ReturnsReplayed()
        {
            // CRIT-1 (1e.1): a timestamp 10 minutes in the FUTURE must be rejected.
            var body = MakeBody();
            var futureTimestamp = FrozenNow.AddMinutes(10);
            var timestampStr = futureTimestamp.ToUnixTimeSeconds().ToString();
            var sig = ComputeExpectedSignature(timestampStr, body);
            var request = BuildRequest(sig, timestampStr);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a future-dated timestamp must be rejected with Replayed by the replay guard");
        }

        [Fact]
        public void ProviderId_IssendgridLowercase()
        {
            var validator = CreateValidator();
            validator.ProviderId.Should().Be("sendgrid");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §3 — Movistar EC webhook validator (HMAC-SHA256)
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class MovistarValidatorTests
    {
        private const string WebhookSecret = "movistar-test-secret-key-32bytes!";

        private static MovistarWebhookSignatureValidator CreateValidator(DateTimeOffset? now = null)
        {
            var options = Options.Create(new MovistarWebhookOptions
            {
                WebhookSecret = WebhookSecret
            });
            return new MovistarWebhookSignatureValidator(options, () => now ?? FrozenNow);
        }

        private static string ComputeExpectedSignature(string secret, byte[] rawBody)
        {
            var key = Encoding.UTF8.GetBytes(secret);
            var hmac = HMACSHA256.HashData(key, rawBody);
            return Convert.ToHexString(hmac).ToLowerInvariant();
        }

        private static HttpRequest BuildRequest(string signature, DateTimeOffset timestamp)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Method = "POST";
            request.Scheme = "https";
            request.Host = new HostString("example.com");
            request.Path = "/api/notifications/delivery-callback/movistar-ec";

            request.Headers["X-Movistar-Signature"] = signature;
            request.Headers["X-Movistar-Timestamp"] = timestamp.ToUnixTimeSeconds().ToString();

            return request;
        }

        private static byte[] MakeBody(string content = "{\"status\":\"DELIVERED\"}") =>
            Encoding.UTF8.GetBytes(content);

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void Validate_ValidHmacSha256Signature_ReturnsValid()
        {
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);
            var request = BuildRequest(sig, RecentTimestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_WrongSecret_ReturnsInvalidSignature()
        {
            var body = MakeBody();
            var sigWithWrongSecret = ComputeExpectedSignature("wrong-secret", body);
            var request = BuildRequest(sigWithWrongSecret, RecentTimestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_TamperedBody_ReturnsInvalidSignature()
        {
            var originalBody = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, originalBody);
            var request = BuildRequest(sig, RecentTimestamp);

            var tamperedBody = MakeBody("{\"status\":\"FAILED\"}");

            var validator = CreateValidator();
            var result = validator.Validate(request, tamperedBody);

            result.Should().Be(WebhookValidationResult.InvalidSignature);
        }

        [Fact]
        public void Validate_MissingSignatureHeader_ReturnsMissingSignature()
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Movistar-Timestamp"] = RecentTimestamp.ToUnixTimeSeconds().ToString();
            // X-Movistar-Signature intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, MakeBody());

            result.Should().Be(WebhookValidationResult.MissingSignature);
        }

        [Fact]
        public void Validate_ReplayAttack_StaleTimestamp_ReturnsReplayed()
        {
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);
            var request = BuildRequest(sig, StaleTimestamp);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a stale timestamp must return Replayed, not InvalidSignature");
        }

        [Fact]
        public void Validate_TimestampExactly5MinAgo_ReturnsReplayed()
        {
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);
            var exactBoundary = FrozenNow.AddMinutes(-5);
            var request = BuildRequest(sig, exactBoundary);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Replayed);
        }

        [Fact]
        public void Validate_TimestampJustUnder5MinAgo_ReturnsValid()
        {
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);
            var recentEnough = FrozenNow.AddSeconds(-299);
            var request = BuildRequest(sig, recentEnough);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_UppercaseHexSignature_ReturnsValid()
        {
            // Providers sometimes send uppercase hex — validator must accept both cases
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body).ToUpperInvariant();
            var request = BuildRequest(sig, RecentTimestamp);

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Valid);
        }

        [Fact]
        public void Validate_FutureDatedTimestamp_ReturnsReplayed()
        {
            // CRIT-1 (1e.1): a timestamp 10 minutes in the FUTURE must be rejected.
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);
            var futureTimestamp = FrozenNow.AddMinutes(10);
            var request = BuildRequest(sig, futureTimestamp);

            var validator = CreateValidator(FrozenNow);
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.Replayed,
                because: "a future-dated timestamp must be rejected with Replayed by the replay guard");
        }

        [Fact]
        public void Validate_MissingTimestampHeader_ReturnsMissingSignature()
        {
            // SUGG-2: explicit test for missing timestamp-only path.
            var body = MakeBody();
            var sig = ComputeExpectedSignature(WebhookSecret, body);

            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Headers["X-Movistar-Signature"] = sig;
            // X-Movistar-Timestamp intentionally absent

            var validator = CreateValidator();
            var result = validator.Validate(request, body);

            result.Should().Be(WebhookValidationResult.MissingSignature,
                because: "a missing timestamp header must be rejected");
        }

        [Fact]
        public void ProviderId_IsmovistarEcLowercase()
        {
            var validator = CreateValidator();
            validator.ProviderId.Should().Be("movistar-ec");
        }
    }
}
