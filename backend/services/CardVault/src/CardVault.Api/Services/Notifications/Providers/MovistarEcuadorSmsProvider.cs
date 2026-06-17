using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CardVault.Infrastructure.Persistence.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardVault.Application.Services.Notifications.Providers;

/// <summary>
/// <see cref="INotificationProvider"/> implementation that sends SMS via Movistar Ecuador.
/// <para>
/// Wire protocol (SOAP or REST) is fully encapsulated — the dispatcher sees only
/// <see cref="ProviderSendResult"/>. Protocol selection is controlled by
/// <see cref="MovistarOptions.UseRestProtocol"/>.
/// </para>
/// <para>
/// SECURITY: The Movistar EC API key is never stored in appsettings.json.
/// It is resolved at call time from the environment variable
/// <c>Notifications__Providers__MovistarEc__ApiKey</c>.
/// </para>
/// <para>
/// DEGRADED MODE: When <see cref="MovistarOptions.DegradedConfirmation"/> is <c>true</c>,
/// an <c>Accepted</c> result sets <see cref="ProviderSendResult.ProviderReportedAt"/> to the
/// current UTC time, signalling the dispatcher to record <c>DeliveredOn</c> immediately
/// (no DLR callback is expected). Logged as a known SBS-evidence limitation.
/// </para>
/// </summary>
public sealed class MovistarEcuadorSmsProvider : INotificationProvider
{
    private readonly HttpClient _httpClient;
    private readonly MovistarOptions _options;
    private readonly IMovistarApiKeyProvider _apiKeyProvider;
    private readonly ILogger<MovistarEcuadorSmsProvider> _logger;

    // ── Permanent error codes (per Movistar EC B2B contract) ──────────────
    private static readonly HashSet<string> PermanentErrorCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "INVALID_MSISDN",  // Destination number invalid / unreachable format
            "BLACKLISTED",     // Number opted out of messages from this sender
            "AUTH_FAILED",     // API key rejected — delivery cannot proceed
        };

    // ── Transient error codes (retry after backoff) ───────────────────────
    private static readonly HashSet<string> TransientErrorCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SYSTEM_BUSY", // Gateway overloaded — retry
            "THROTTLED",   // Rate-limited by the gateway — retry
        };

    /// <inheritdoc />
    public string ProviderId => "movistar-ec";

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <summary>
    /// Movistar EC handles exclusively Ecuadorian mobile numbers (E.164 prefix <c>+593</c>).
    /// </summary>
    public bool CanHandle(string destinationE164OrEmail)
        => destinationE164OrEmail.StartsWith("+593", StringComparison.Ordinal);

    /// <summary>Constructs the provider. DI-friendly.</summary>
    public MovistarEcuadorSmsProvider(
        HttpClient httpClient,
        IOptions<MovistarOptions> options,
        IMovistarApiKeyProvider apiKeyProvider,
        ILogger<MovistarEcuadorSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
        => _options.UseRestProtocol
            ? SendRestAsync(request, ct)
            : SendSoapAsync(request, ct);

    // ─────────────────────────────────────────────────────────────────────
    // SOAP path
    // ─────────────────────────────────────────────────────────────────────

    private async Task<ProviderSendResult> SendSoapAsync(
        NotificationSendRequest request,
        CancellationToken ct)
    {
        var apiKey = _apiKeyProvider.GetApiKey();
        var soapXml = BuildSoapEnvelope(apiKey, request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.SoapEndpointPath);
        httpRequest.Content = new StringContent(soapXml, Encoding.UTF8, "text/xml");
        httpRequest.Headers.TryAddWithoutValidation("SOAPAction", "\"SendMessage\"");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex,
                "Movistar EC SOAP request timed out for delivery {DeliveryId}", request.DeliveryId);
            return TransientFailure("TIMEOUT", "Network timeout communicating with Movistar EC");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Movistar EC SOAP HTTP transport error for delivery {DeliveryId}", request.DeliveryId);
            return TransientFailure("HTTP_ERROR", ex.Message);
        }

        return await ClassifySoapResponseAsync(response, request.DeliveryId, ct).ConfigureAwait(false);
    }

    private async Task<ProviderSendResult> ClassifySoapResponseAsync(
        HttpResponseMessage response,
        Guid deliveryId,
        CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;

        string? body = null;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { /* ignore read errors — proceed with null body */ }

        // HTTP 429 → TransientFailure (ADR-3)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "Movistar EC rate-limited delivery {DeliveryId}: HTTP 429", deliveryId);
            return TransientFailure("RATE_LIMITED", body);
        }

        // HTTP 5xx → TransientFailure
        if (statusCode >= 500)
        {
            var errorCode = ExtractXmlErrorCode(body);
            _logger.LogWarning(
                "Movistar EC server error for delivery {DeliveryId}: HTTP {StatusCode}",
                deliveryId, statusCode);
            return TransientFailure(errorCode ?? $"MOVISTAR_{statusCode}", body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return TransientFailure("EMPTY_RESPONSE",
                "No response body received from Movistar EC gateway");
        }

        // Parse SOAP envelope
        XDocument? doc = null;
        try { doc = XDocument.Parse(body); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse Movistar EC SOAP response for delivery {DeliveryId}", deliveryId);
            return statusCode >= 400
                ? PermanentFailure("PARSE_ERROR", body)
                : TransientFailure("PARSE_ERROR", body);
        }

        // Check for SOAP Fault element
        XNamespace soapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
        var fault = doc.Descendants(soapEnv + "Fault").FirstOrDefault()
                    // Some implementations use un-namespaced Fault
                    ?? doc.Descendants("Fault").FirstOrDefault();

        if (fault is not null)
        {
            var faultCode = fault.Element("faultcode")?.Value
                            ?? fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultcode")?.Value
                            ?? string.Empty;
            var errorCode = ExtractFaultDetailErrorCode(fault);
            return ClassifySoapFault(faultCode, errorCode, body, deliveryId);
        }

        // Non-fault success response: extract MessageId
        var messageId = ExtractXmlValue(doc, "MessageId");
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            return BuildAcceptedResult(messageId, deliveryId);
        }

        // Non-fault error response (HTTP 4xx without fault element) — check errorCode
        if (statusCode >= 400)
        {
            var errorCode = ExtractXmlErrorCode(body);
            if (errorCode is not null)
            {
                if (TransientErrorCodes.Contains(errorCode))
                    return TransientFailure(errorCode, body);
                if (PermanentErrorCodes.Contains(errorCode))
                    return PermanentFailure(errorCode, body);
            }

            return PermanentFailure(errorCode ?? $"MOVISTAR_{statusCode}", body);
        }

        // 2xx but no MessageId — unexpected
        _logger.LogWarning(
            "Movistar EC returned 2xx but no MessageId for delivery {DeliveryId}", deliveryId);
        return TransientFailure("MISSING_MESSAGE_ID", body);
    }

    private ProviderSendResult ClassifySoapFault(
        string faultCode,
        string? detailErrorCode,
        string? body,
        Guid deliveryId)
    {
        // Specific application error codes take precedence over the SOAP fault code
        if (detailErrorCode is not null)
        {
            if (TransientErrorCodes.Contains(detailErrorCode))
            {
                _logger.LogWarning(
                    "Movistar EC transient error in SOAP fault for delivery {DeliveryId}: {ErrorCode}",
                    deliveryId, detailErrorCode);
                return TransientFailure(detailErrorCode, body);
            }

            if (PermanentErrorCodes.Contains(detailErrorCode))
            {
                _logger.LogWarning(
                    "Movistar EC permanent error in SOAP fault for delivery {DeliveryId}: {ErrorCode}",
                    deliveryId, detailErrorCode);
                return PermanentFailure(detailErrorCode, body);
            }
        }

        // Classify by SOAP fault code: soap:Server (server-side) → TransientFailure
        if (faultCode.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Movistar EC SOAP server fault for delivery {DeliveryId}: faultcode={FaultCode}",
                deliveryId, faultCode);
            return TransientFailure(detailErrorCode ?? "SOAP_SERVER_FAULT", body);
        }

        // soap:Client (client-side, bad request) → PermanentFailure
        _logger.LogWarning(
            "Movistar EC SOAP client fault for delivery {DeliveryId}: faultcode={FaultCode}",
            deliveryId, faultCode);
        return PermanentFailure(detailErrorCode ?? "SOAP_CLIENT_FAULT", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // REST path
    // ─────────────────────────────────────────────────────────────────────

    private async Task<ProviderSendResult> SendRestAsync(
        NotificationSendRequest request,
        CancellationToken ct)
    {
        var apiKey = _apiKeyProvider.GetApiKey();
        var payload = JsonSerializer.Serialize(new
        {
            apiKey,
            from = _options.SenderId,
            to = request.Destination,
            message = request.RenderedBody,
            messageRef = request.DeliveryId.ToString("N")
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.RestEndpointPath);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex,
                "Movistar EC REST request timed out for delivery {DeliveryId}", request.DeliveryId);
            return TransientFailure("TIMEOUT", "Network timeout communicating with Movistar EC (REST)");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Movistar EC REST HTTP transport error for delivery {DeliveryId}", request.DeliveryId);
            return TransientFailure("HTTP_ERROR", ex.Message);
        }

        return await ClassifyRestResponseAsync(response, request.DeliveryId, ct).ConfigureAwait(false);
    }

    private async Task<ProviderSendResult> ClassifyRestResponseAsync(
        HttpResponseMessage response,
        Guid deliveryId,
        CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;

        string? body = null;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { /* ignore */ }

        // HTTP 429 → TransientFailure (ADR-3)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return TransientFailure("RATE_LIMITED", body);

        // HTTP 5xx → TransientFailure
        if (statusCode >= 500)
        {
            var errorCode = ExtractJsonErrorCode(body);
            return TransientFailure(errorCode ?? $"MOVISTAR_{statusCode}", body);
        }

        // Try to parse JSON body
        string? messageId = null;
        string? jsonErrorCode = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("messageId", out var id))
                    messageId = id.GetString();
                if (doc.RootElement.TryGetProperty("errorCode", out var err))
                    jsonErrorCode = err.GetString();
            }
            catch { /* ignore parse errors */ }
        }

        // Check application-level error codes
        if (jsonErrorCode is not null)
        {
            if (TransientErrorCodes.Contains(jsonErrorCode))
                return TransientFailure(jsonErrorCode, body);
            if (PermanentErrorCodes.Contains(jsonErrorCode))
                return PermanentFailure(jsonErrorCode, body);
        }

        // Successful response with messageId
        if (!string.IsNullOrWhiteSpace(messageId))
            return BuildAcceptedResult(messageId, deliveryId);

        // 4xx (not 429) → PermanentFailure
        if (statusCode >= 400)
            return PermanentFailure(jsonErrorCode ?? $"MOVISTAR_{statusCode}", body);

        // 2xx but no messageId
        _logger.LogWarning(
            "Movistar EC REST returned 2xx but no messageId for delivery {DeliveryId}", deliveryId);
        return TransientFailure("MISSING_MESSAGE_ID", body);
    }

    // ─────────────────────────────────────────────────────────────────────
    // SOAP building helpers
    // ─────────────────────────────────────────────────────────────────────

    private string BuildSoapEnvelope(string apiKey, NotificationSendRequest request)
    {
        XNamespace soapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace ns = _options.SoapNamespace;

        var envelope = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(soapEnv + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", soapEnv.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ns", ns.NamespaceName),
                new XElement(soapEnv + "Header",
                    new XElement(ns + "Credentials",
                        new XElement(ns + "ApiKey", apiKey))),
                new XElement(soapEnv + "Body",
                    new XElement(ns + "SendMessage",
                        new XElement(ns + "SenderId", _options.SenderId),
                        new XElement(ns + "To", request.Destination),
                        new XElement(ns + "Body", request.RenderedBody),
                        new XElement(ns + "MessageRef", request.DeliveryId.ToString("N"))))));

        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    private static string? ExtractXmlValue(XDocument doc, string localName)
        => doc.Descendants()
              .FirstOrDefault(e => e.Name.LocalName == localName)
              ?.Value;

    private static string? ExtractFaultDetailErrorCode(XElement fault)
        => fault.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName is "errorCode" or "ErrorCode")
                ?.Value;

    private static string? ExtractXmlErrorCode(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            var doc = XDocument.Parse(body);
            return doc.Descendants()
                      .FirstOrDefault(e => e.Name.LocalName is "errorCode" or "ErrorCode")
                      ?.Value;
        }
        catch { return null; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON parsing helpers
    // ─────────────────────────────────────────────────────────────────────

    private static string? ExtractJsonErrorCode(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorCode", out var code))
                return code.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Result builders
    // ─────────────────────────────────────────────────────────────────────

    private ProviderSendResult BuildAcceptedResult(string messageId, Guid deliveryId)
    {
        DateTimeOffset? providerReportedAt = null;

        if (_options.DegradedConfirmation)
        {
            // Movistar EC in degraded mode: accepted = delivered (no DLR callback expected).
            // ProviderReportedAt signals the dispatcher to set DeliveredOn immediately.
            // This is a known SBS-evidence limitation — every degraded confirmation is logged.
            providerReportedAt = DateTimeOffset.UtcNow;
            _logger.LogWarning(
                "Movistar EC degraded confirmation for delivery {DeliveryId}: no DLR callback " +
                "expected — DeliveredOn will be set at send time (SBS-evidence limitation). " +
                "MessageId={MessageId}",
                deliveryId, messageId);
        }

        return new ProviderSendResult(
            ProviderOutcome.Accepted,
            ProviderReference: messageId,
            ErrorCode: null,
            ErrorMessage: null,
            ProviderReportedAt: providerReportedAt);
    }

    private static ProviderSendResult TransientFailure(string? errorCode, string? message)
        => new(ProviderOutcome.TransientFailure, null, errorCode, message, DateTimeOffset.UtcNow);

    private static ProviderSendResult PermanentFailure(string? errorCode, string? message)
        => new(ProviderOutcome.PermanentFailure, null, errorCode, message, DateTimeOffset.UtcNow);
}

// ── Auth token abstraction ────────────────────────────────────────────────────

/// <summary>
/// Resolves the Movistar Ecuador API key.
/// Production implementation reads from environment variable.
/// Test implementations inject a fixed string.
/// </summary>
public interface IMovistarApiKeyProvider
{
    string GetApiKey();
}

/// <summary>
/// Resolves the Movistar EC API key from the environment variable
/// <c>Notifications__Providers__MovistarEc__ApiKey</c>.
/// </summary>
public sealed class EnvironmentMovistarApiKeyProvider : IMovistarApiKeyProvider
{
    public string GetApiKey()
        => Environment.GetEnvironmentVariable("Notifications__Providers__MovistarEc__ApiKey")
           ?? string.Empty;
}
