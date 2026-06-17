using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardVault.Infrastructure.Persistence.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardVault.Application.Services.Notifications.Providers;

/// <summary>
/// <see cref="INotificationProvider"/> implementation that sends SMS via Twilio.
/// <para>
/// SECURITY: The Twilio Auth Token is never stored in appsettings.json.
/// It is resolved at call time from the environment variable
/// <c>Notifications__Providers__Twilio__AuthToken</c>.
/// </para>
/// </summary>
public sealed class TwilioSmsProvider : INotificationProvider
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ITwilioAuthTokenProvider _authTokenProvider;
    private readonly ILogger<TwilioSmsProvider> _logger;

    /// <summary>Stable provider identifier.</summary>
    public string ProviderId => "twilio";

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <summary>Twilio is the global SMS fallback — handles any destination.</summary>
    public bool CanHandle(string destinationE164OrEmail) => true;

    /// <summary>
    /// Constructs the provider.
    /// </summary>
    public TwilioSmsProvider(
        HttpClient httpClient,
        IOptions<TwilioOptions> options,
        ITwilioAuthTokenProvider authTokenProvider,
        ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _authTokenProvider = authTokenProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProviderSendResult> SendAsync(
        NotificationSendRequest request,
        CancellationToken ct)
    {
        var authToken = _authTokenProvider.GetAuthToken();
        var endpoint = $"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

        // Build form-encoded payload (Twilio uses application/x-www-form-urlencoded)
        var formFields = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["To"] = request.Destination,
            ["Body"] = request.RenderedBody
        };

        if (!string.IsNullOrWhiteSpace(_options.StatusCallbackUrl))
            formFields["StatusCallback"] = _options.StatusCallbackUrl;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);

        // Basic auth: AccountSid:AuthToken base64-encoded
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.AccountSid}:{authToken}"));
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        httpRequest.Content = new FormUrlEncodedContent(formFields);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Twilio request timed out for delivery {DeliveryId}", request.DeliveryId);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: "TIMEOUT",
                ErrorMessage: "Network timeout communicating with Twilio",
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Twilio HTTP error for delivery {DeliveryId}", request.DeliveryId);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: "HTTP_ERROR",
                ErrorMessage: ex.Message,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        return await ClassifyResponseAsync(response, request.DeliveryId, ct).ConfigureAwait(false);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Permanent Twilio error codes that indicate the destination cannot receive messages.
    /// See https://www.twilio.com/docs/api/errors
    /// </summary>
    private static readonly HashSet<int> PermanentErrorCodes = new()
    {
        21211, // Invalid 'To' Phone Number
        21610, // Message cannot be sent to a number that has opted out
        21614, // 'To' number is not a valid mobile number
        21408, // Permission to send an SMS has not been enabled for the region
        21612, // The 'To' phone number is not currently reachable
    };

    private async Task<ProviderSendResult> ClassifyResponseAsync(
        HttpResponseMessage response,
        Guid deliveryId,
        CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;

        // Read response body (best-effort)
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        // Success (201 Created)
        if (response.IsSuccessStatusCode)
        {
            var sid = ExtractSid(body);
            return new ProviderSendResult(
                ProviderOutcome.Accepted,
                ProviderReference: sid,
                ErrorCode: null,
                ErrorMessage: null,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // Parse Twilio error code if present
        var twilioCode = ExtractTwilioCode(body);

        // 429 and Twilio transient codes → TransientFailure (ADR-3)
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || twilioCode is 20429 or 20503)
        {
            _logger.LogWarning(
                "Twilio rate-limited delivery {DeliveryId}: HTTP {StatusCode}, code {TwilioCode}",
                deliveryId, statusCode, twilioCode);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: twilioCode?.ToString() ?? "RATE_LIMITED",
                ErrorMessage: body,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // 5xx → TransientFailure
        if (statusCode >= 500)
        {
            _logger.LogWarning(
                "Twilio server error for delivery {DeliveryId}: HTTP {StatusCode}",
                deliveryId, statusCode);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: twilioCode?.ToString() ?? $"TWILIO_{statusCode}",
                ErrorMessage: body,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // Check permanent Twilio error codes (specific 4xx)
        if (twilioCode.HasValue && PermanentErrorCodes.Contains(twilioCode.Value))
        {
            _logger.LogWarning(
                "Twilio permanent failure for delivery {DeliveryId}: code {TwilioCode}",
                deliveryId, twilioCode);
            return new ProviderSendResult(
                ProviderOutcome.PermanentFailure,
                ProviderReference: null,
                ErrorCode: twilioCode.ToString(),
                ErrorMessage: body,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // All other 4xx → PermanentFailure
        _logger.LogWarning(
            "Twilio permanent 4xx for delivery {DeliveryId}: HTTP {StatusCode}, code {TwilioCode}",
            deliveryId, statusCode, twilioCode);
        return new ProviderSendResult(
            ProviderOutcome.PermanentFailure,
            ProviderReference: null,
            ErrorCode: twilioCode?.ToString() ?? $"TWILIO_{statusCode}",
            ErrorMessage: body,
            ProviderReportedAt: DateTimeOffset.UtcNow);
    }

    private static string? ExtractSid(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("sid", out var sid))
                return sid.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    private static int? ExtractTwilioCode(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("code", out var code))
                return code.GetInt32();
        }
        catch { /* ignore */ }
        return null;
    }
}

// ── Auth token abstraction ────────────────────────────────────────────────────

/// <summary>
/// Resolves the Twilio Auth Token.
/// Production implementation reads from environment variable.
/// Test implementations inject a fixed string.
/// </summary>
public interface ITwilioAuthTokenProvider
{
    string GetAuthToken();
}

/// <summary>
/// Resolves the Twilio Auth Token from the environment variable
/// <c>Notifications__Providers__Twilio__AuthToken</c>.
/// </summary>
public sealed class EnvironmentTwilioAuthTokenProvider : ITwilioAuthTokenProvider
{
    public string GetAuthToken()
        => Environment.GetEnvironmentVariable("Notifications__Providers__Twilio__AuthToken")
           ?? string.Empty;
}
