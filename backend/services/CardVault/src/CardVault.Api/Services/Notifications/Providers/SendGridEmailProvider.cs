using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardVault.Infrastructure.Persistence.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardVault.Api.Services.Notifications.Providers;

/// <summary>
/// <see cref="INotificationProvider"/> implementation that sends transactional email via SendGrid.
/// <para>
/// SECURITY: The SendGrid API key is never stored in appsettings.json.
/// It is resolved at call time from the environment variable
/// <c>Notifications__Providers__SendGrid__ApiKey</c>.
/// </para>
/// </summary>
public sealed class SendGridEmailProvider : INotificationProvider
{
    private readonly HttpClient _httpClient;
    private readonly SendGridOptions _options;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly ILogger<SendGridEmailProvider> _logger;

    /// <summary>Stable provider identifier used throughout the system.</summary>
    public string ProviderId => "sendgrid";

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Email;

    /// <summary>
    /// Constructs the provider. Used directly when the API key is supplied via an
    /// <see cref="IApiKeyProvider"/> abstraction (allows test injection).
    /// </summary>
    public SendGridEmailProvider(
        HttpClient httpClient,
        IOptions<SendGridOptions> options,
        IApiKeyProvider apiKeyProvider,
        ILogger<SendGridEmailProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanHandle(string destinationE164OrEmail)
        => !string.IsNullOrWhiteSpace(destinationE164OrEmail);

    /// <inheritdoc />
    public async Task<ProviderSendResult> SendAsync(
        NotificationSendRequest request,
        CancellationToken ct)
    {
        var apiKey = _apiKeyProvider.GetApiKey();

        // Build SendGrid v3 Mail Send payload
        var payload = BuildPayload(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v3/mail/send");
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "SendGrid request timed out for delivery {DeliveryId}", request.DeliveryId);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: "TIMEOUT",
                ErrorMessage: "Network timeout communicating with SendGrid",
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "SendGrid HTTP error for delivery {DeliveryId}", request.DeliveryId);
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

    private SendGridMailPayload BuildPayload(NotificationSendRequest request)
    {
        return new SendGridMailPayload(
            From: new SendGridEmail(_options.FromEmail, _options.FromName),
            Personalizations: new[]
            {
                new SendGridPersonalization(To: new[] { new SendGridEmail(request.Destination) })
            },
            Subject: request.RenderedSubject,
            Content: new[]
            {
                new SendGridContent("text/plain", request.RenderedBody)
            });
    }

    private async Task<ProviderSendResult> ClassifyResponseAsync(
        HttpResponseMessage response,
        Guid deliveryId,
        CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var messageId = GetMessageId(response);

        if (response.IsSuccessStatusCode)
        {
            return new ProviderSendResult(
                ProviderOutcome.Accepted,
                ProviderReference: messageId,
                ErrorCode: null,
                ErrorMessage: null,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // Read error details (best-effort, don't throw)
        string? errorBody = null;
        try
        {
            errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch { /* ignore read errors */ }

        // 429 → TransientFailure (ADR-3: rate-limit is retryable)
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "SendGrid rate-limited delivery {DeliveryId}: {StatusCode}",
                deliveryId, statusCode);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: "RATE_LIMITED",
                ErrorMessage: errorBody,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // 5xx → TransientFailure
        if (statusCode >= 500)
        {
            _logger.LogWarning(
                "SendGrid server error for delivery {DeliveryId}: {StatusCode}",
                deliveryId, statusCode);
            return new ProviderSendResult(
                ProviderOutcome.TransientFailure,
                ProviderReference: null,
                ErrorCode: $"SENDGRID_{statusCode}",
                ErrorMessage: errorBody,
                ProviderReportedAt: DateTimeOffset.UtcNow);
        }

        // 4xx (except 429) → PermanentFailure
        // 401/403 are permanent but alert-worthy
        if (statusCode is 401 or 403)
        {
            _logger.LogError(
                "SendGrid authentication failure for delivery {DeliveryId}: {StatusCode} — check API key configuration",
                deliveryId, statusCode);
        }
        else
        {
            _logger.LogWarning(
                "SendGrid permanent failure for delivery {DeliveryId}: {StatusCode}",
                deliveryId, statusCode);
        }

        return new ProviderSendResult(
            ProviderOutcome.PermanentFailure,
            ProviderReference: null,
            ErrorCode: $"SENDGRID_{statusCode}",
            ErrorMessage: errorBody,
            ProviderReportedAt: DateTimeOffset.UtcNow);
    }

    private static string? GetMessageId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Message-Id", out var values))
            return values.FirstOrDefault();
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// ── Payload records ───────────────────────────────────────────────────────────

internal sealed record SendGridMailPayload(
    [property: JsonPropertyName("from")] SendGridEmail From,
    [property: JsonPropertyName("personalizations")] IReadOnlyList<SendGridPersonalization> Personalizations,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("content")] IReadOnlyList<SendGridContent> Content);

internal sealed record SendGridPersonalization(
    [property: JsonPropertyName("to")] IReadOnlyList<SendGridEmail> To);

internal sealed record SendGridEmail(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null);

internal sealed record SendGridContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value);

// ── API key abstraction (allows test injection without env var side-effects) ──

/// <summary>
/// Resolves the SendGrid API key.
/// Production implementation reads from environment variable.
/// Test implementations inject a fixed string.
/// </summary>
public interface IApiKeyProvider
{
    string GetApiKey();
}

/// <summary>
/// Resolves the SendGrid API key from the environment variable
/// <c>Notifications__Providers__SendGrid__ApiKey</c>.
/// </summary>
public sealed class EnvironmentSendGridApiKeyProvider : IApiKeyProvider
{
    public string GetApiKey()
        => Environment.GetEnvironmentVariable("Notifications__Providers__SendGrid__ApiKey")
           ?? string.Empty;
}
