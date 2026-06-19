using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CardVault.Infrastructure.Notifications;
using CardVault.Infrastructure.Notifications.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Features.Notifications.Providers;

/// <summary>
/// HTTP-mocked unit tests for <see cref="SendGridEmailProvider"/>.
/// NEVER calls real SendGrid endpoints.
/// </summary>
public sealed class SendGridEmailProviderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SendGridEmailProvider CreateProvider(
        HttpMessageHandler handler,
        string? apiKey = "test-api-key",
        string fromEmail = "noreply@test.com",
        string fromName = "Test")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sendgrid.com")
        };

        var options = Options.Create(new SendGridOptions
        {
            FromEmail = fromEmail,
            FromName = fromName
        });

        var apiKeyEnvProvider = new StaticApiKeyProvider(apiKey ?? string.Empty);

        return new SendGridEmailProvider(httpClient, options, apiKeyEnvProvider, NullLogger<SendGridEmailProvider>.Instance);
    }

    private static NotificationSendRequest MakeRequest() => new(
        DeliveryId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        Channel: CardVault.Infrastructure.Persistence.Notifications.NotificationChannel.Email,
        Destination: "cardholder@example.com",
        RenderedSubject: "Your OTP code",
        RenderedBody: "Your code is 123456",
        TemplateType: "Otp",
        Locale: "es-EC");

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_HappyPath_ReturnsAcceptedWithProviderReference()
    {
        var messageId = "msg_abc123xyz";
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted,
            responseBody: null,
            responseHeaders: new Dictionary<string, string>
            {
                ["X-Message-Id"] = messageId
            });

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().Be(messageId);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_HappyPath_PostsToSendGridV3MailSend()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted,
            responseBody: null,
            responseHeaders: new Dictionary<string, string> { ["X-Message-Id"] = "ref" });

        var provider = CreateProvider(handler);
        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v3/mail/send");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_HappyPath_IncludesAuthorizationBearerHeader()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted,
            responseBody: null,
            responseHeaders: new Dictionary<string, string> { ["X-Message-Id"] = "ref" });

        var provider = CreateProvider(handler, apiKey: "SG-test-key");
        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("SG-test-key");
    }

    [Fact]
    public async Task SendAsync_HappyPath_BodyDoesNotContainUnmaskedPan()
    {
        // PCI safety: unmasked PANs must never appear in outbound request bodies
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted,
            responseBody: null,
            responseHeaders: new Dictionary<string, string> { ["X-Message-Id"] = "ref" });

        var provider = CreateProvider(handler);
        var request = MakeRequest() with
        {
            // Simulates a caller accidentally putting a PAN-like value in the body
            // The provider itself should not inject such data
            RenderedBody = "Your masked card: ****1234"
        };

        await provider.SendAsync(request, CancellationToken.None);

        // Verify the request body does NOT contain a full 16-digit sequence
        var sentBody = handler.LastRequestBody;
        sentBody.Should().NotMatchRegex(@"\b\d{16}\b",
            "unmasked PANs must never appear in provider request bodies");
    }

    // ── 5xx → TransientFailure ─────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task SendAsync_ServerError_ReturnsTransientFailure(int statusCode)
    {
        var handler = new MockHttpMessageHandler(
            (HttpStatusCode)statusCode,
            responseBody: """{"errors":[{"message":"Internal server error"}]}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            $"HTTP {statusCode} from SendGrid must be treated as transient (retryable)");
    }

    // ── 4xx → PermanentFailure ────────────────────────────────────────────

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    public async Task SendAsync_ClientError4xx_ReturnsPermanentFailure(int statusCode)
    {
        var handler = new MockHttpMessageHandler(
            (HttpStatusCode)statusCode,
            responseBody: """{"errors":[{"message":"Bad Request"}]}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure,
            $"HTTP {statusCode} from SendGrid must be treated as permanent failure");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task SendAsync_Unauthorized_ReturnsPermanentFailure(int statusCode)
    {
        var handler = new MockHttpMessageHandler(
            (HttpStatusCode)statusCode,
            responseBody: """{"errors":[{"message":"Unauthorized"}]}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure,
            $"HTTP {statusCode} auth error is permanent (alert-worthy) for this delivery");
        result.ErrorCode.Should().NotBeNullOrEmpty("auth failures should include an error code");
    }

    // ── 429 → TransientFailure (ADR-3: rate-limit is retryable) ──────────────

    [Fact]
    public async Task SendAsync_RateLimited_ReturnsTransientFailure()
    {
        var handler = new MockHttpMessageHandler(
            HttpStatusCode.TooManyRequests,
            responseBody: """{"errors":[{"message":"Too many requests"}]}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "429 rate-limit is transient per ADR-3 — must be retried, not dead-lettered");
    }

    // ── Network timeout → TransientFailure ───────────────────────────────────

    [Fact]
    public async Task SendAsync_NetworkTimeout_ReturnsTransientFailure()
    {
        var handler = new TimeoutMockHttpMessageHandler();

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "network timeouts are transient — must be retried");
        result.ErrorCode.Should().NotBeNullOrEmpty();
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderId_IsConstantSendgrid()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted, null);
        var provider = CreateProvider(handler);

        provider.ProviderId.Should().Be("sendgrid");
    }

    [Fact]
    public void Channel_IsEmail()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted, null);
        var provider = CreateProvider(handler);

        provider.Channel.Should().Be(CardVault.Infrastructure.Persistence.Notifications.NotificationChannel.Email);
    }

    [Fact]
    public void CanHandle_NonEmptyEmail_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted, null);
        var provider = CreateProvider(handler);

        provider.CanHandle("user@example.com").Should().BeTrue();
        provider.CanHandle("another@domain.co.uk").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_EmptyOrNull_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted, null);
        var provider = CreateProvider(handler);

        provider.CanHandle(string.Empty).Should().BeFalse();
    }

    // ── Implements interface ──────────────────────────────────────────────────

    [Fact]
    public void SendGridEmailProvider_ImplementsINotificationProvider()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted, null);
        var provider = CreateProvider(handler);
        provider.Should().BeAssignableTo<INotificationProvider>();
    }
}

// ── Mock HTTP infrastructure ──────────────────────────────────────────────────

file sealed class StaticApiKeyProvider(string apiKey) : CardVault.Infrastructure.Notifications.Providers.IApiKeyProvider
{
    public string GetApiKey() => apiKey;
}

file sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string? _responseBody;
    private readonly Dictionary<string, string> _responseHeaders;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastRequestBody { get; private set; } = string.Empty;

    public MockHttpMessageHandler(
        HttpStatusCode statusCode,
        string? responseBody,
        Dictionary<string, string>? responseHeaders = null)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
        _responseHeaders = responseHeaders ?? new();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new HttpResponseMessage(_statusCode);

        if (_responseBody != null)
            response.Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json");

        foreach (var (key, value) in _responseHeaders)
            response.Headers.TryAddWithoutValidation(key, value);

        return response;
    }
}

file sealed class TimeoutMockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new TaskCanceledException("Simulated network timeout");
}
