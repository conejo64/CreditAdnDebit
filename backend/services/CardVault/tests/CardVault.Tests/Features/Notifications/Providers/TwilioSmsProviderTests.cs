using System.Net;
using System.Text;
using System.Text.Json;
using CardVault.Api.Services.Notifications;
using CardVault.Api.Services.Notifications.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Features.Notifications.Providers;

/// <summary>
/// HTTP-mocked unit tests for <see cref="TwilioSmsProvider"/>.
/// NEVER calls real Twilio endpoints.
/// </summary>
public sealed class TwilioSmsProviderTests
{
    private const string TestAccountSid = "ACtest00000000000000000000000000";
    private const string TestAuthToken = "test-auth-token-not-a-real-token";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TwilioSmsProvider CreateProvider(
        HttpMessageHandler handler,
        string accountSid = TestAccountSid,
        string authToken = TestAuthToken,
        string fromNumber = "+15550001234")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.twilio.com")
        };

        var options = Options.Create(new TwilioOptions
        {
            AccountSid = accountSid,
            FromNumber = fromNumber
        });

        var authTokenProvider = new StaticTwilioAuthTokenProvider(authToken);

        return new TwilioSmsProvider(httpClient, options, authTokenProvider, NullLogger<TwilioSmsProvider>.Instance);
    }

    private static NotificationSendRequest MakeRequest() => new(
        DeliveryId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        Channel: CardVault.Infrastructure.Persistence.Notifications.NotificationChannel.Sms,
        Destination: "+15550009999",
        RenderedSubject: string.Empty,
        RenderedBody: "Your OTP is 123456",
        TemplateType: "Otp",
        Locale: "es-EC");

    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_HappyPath_ReturnsAcceptedWithSidAsProviderReference()
    {
        var sid = "SM12345678901234567890123456789012";
        var handler = new TwilioMockHandler(HttpStatusCode.Created,
            responseBody: $$"""{"sid":"{{sid}}","status":"queued","to":"+15550009999"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().Be(sid);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_HappyPath_PostsToCorrectTwilioEndpoint()
    {
        var sid = "SMtest";
        var handler = new TwilioMockHandler(HttpStatusCode.Created,
            responseBody: $$"""{"sid":"{{sid}}","status":"queued"}""");

        var provider = CreateProvider(handler);
        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.PathAndQuery.Should()
            .Be($"/2010-04-01/Accounts/{TestAccountSid}/Messages.json");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_HappyPath_UsesBasicAuthWithAccountSidAndAuthToken()
    {
        var handler = new TwilioMockHandler(HttpStatusCode.Created,
            responseBody: """{"sid":"SMtest","status":"queued"}""");

        var provider = CreateProvider(handler);
        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");

        var decoded = Encoding.UTF8.GetString(
            Convert.FromBase64String(handler.LastRequest.Headers.Authorization.Parameter!));
        decoded.Should().Be($"{TestAccountSid}:{TestAuthToken}",
            "Twilio requires Basic auth with AccountSid:AuthToken");
    }

    [Fact]
    public async Task SendAsync_HappyPath_BodyDoesNotContainOtpSecret()
    {
        // PCI safety: OTP seeds/secrets must never be in outbound request bodies.
        // OTP CODE (display) is allowed, but never a raw numeric secret that could be a PAN.
        var handler = new TwilioMockHandler(HttpStatusCode.Created,
            responseBody: """{"sid":"SMtest","status":"queued"}""");

        var provider = CreateProvider(handler);
        var request = MakeRequest() with { RenderedBody = "Your code: 123456" };
        await provider.SendAsync(request, CancellationToken.None);

        // Verify the form-encoded body doesn't contain a 16-digit PAN sequence
        var sentBody = handler.LastRequestBody;
        sentBody.Should().NotMatchRegex(@"\b\d{16}\b",
            "16-digit sequences (potential PANs) must never appear in provider request bodies");
    }

    // ── 5xx → TransientFailure ─────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task SendAsync_ServerError_ReturnsTransientFailure(int statusCode)
    {
        var handler = new TwilioMockHandler(
            (HttpStatusCode)statusCode,
            responseBody: """{"message":"Internal server error"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            $"HTTP {statusCode} from Twilio must be treated as transient");
    }

    // ── 429 → TransientFailure ─────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_RateLimited429_ReturnsTransientFailure()
    {
        var handler = new TwilioMockHandler(
            HttpStatusCode.TooManyRequests,
            responseBody: """{"code":20429,"message":"Too many requests"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "429 is transient per ADR-3 — must be retried");
    }

    [Theory]
    [InlineData(20429)]
    [InlineData(20503)]
    public async Task SendAsync_TwilioTransientErrorCodes_ReturnTransientFailure(int twilioCode)
    {
        var handler = new TwilioMockHandler(
            HttpStatusCode.TooManyRequests,
            responseBody: $$"""{"code":{{twilioCode}},"message":"Transient error"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            $"Twilio error code {twilioCode} is transient and must be retried");
    }

    // ── Permanent Twilio error codes ──────────────────────────────────────

    [Theory]
    [InlineData(21211, "invalid destination number")]
    [InlineData(21610, "unsubscribed number")]
    [InlineData(21614, "not a mobile number")]
    [InlineData(21408, "number not enabled for region")]
    [InlineData(21612, "number cannot receive SMS")]
    public async Task SendAsync_TwilioPermanentErrorCodes_ReturnPermanentFailure(int twilioCode, string description)
    {
        var handler = new TwilioMockHandler(
            HttpStatusCode.BadRequest,
            responseBody: $$"""{"code":{{twilioCode}},"message":"{{description}}","more_info":"https://twilio.com/docs/errors/{{twilioCode}}"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure,
            $"Twilio code {twilioCode} ({description}) is permanent — do not retry");
        result.ErrorCode.Should().Be(twilioCode.ToString());
    }

    // ── Other 4xx → PermanentFailure ─────────────────────────────────────

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task SendAsync_Other4xx_ReturnsPermanentFailure(int statusCode)
    {
        var handler = new TwilioMockHandler(
            (HttpStatusCode)statusCode,
            responseBody: """{"code":99999,"message":"Unknown error"}""");

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure,
            $"HTTP {statusCode} (not 429) is permanent for Twilio");
    }

    // ── Network timeout → TransientFailure ───────────────────────────────

    [Fact]
    public async Task SendAsync_NetworkTimeout_ReturnsTransientFailure()
    {
        var handler = new TwilioTimeoutMockHandler();
        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "network timeouts are transient — must be retried");
        result.ErrorCode.Should().NotBeNullOrEmpty();
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderId_IsConstantTwilio()
    {
        var handler = new TwilioMockHandler(HttpStatusCode.Created, """{"sid":"x"}""");
        var provider = CreateProvider(handler);
        provider.ProviderId.Should().Be("twilio");
    }

    [Fact]
    public void Channel_IsSms()
    {
        var handler = new TwilioMockHandler(HttpStatusCode.Created, """{"sid":"x"}""");
        var provider = CreateProvider(handler);
        provider.Channel.Should().Be(CardVault.Infrastructure.Persistence.Notifications.NotificationChannel.Sms);
    }

    [Fact]
    public void CanHandle_AlwaysReturnsTrue()
    {
        // Twilio is the global SMS fallback — handles any destination
        var handler = new TwilioMockHandler(HttpStatusCode.Created, """{"sid":"x"}""");
        var provider = CreateProvider(handler);

        provider.CanHandle("+15550009999").Should().BeTrue();
        provider.CanHandle("+593987654321").Should().BeTrue();
        provider.CanHandle("+441234567890").Should().BeTrue();
    }

    [Fact]
    public void TwilioSmsProvider_ImplementsINotificationProvider()
    {
        var handler = new TwilioMockHandler(HttpStatusCode.Created, """{"sid":"x"}""");
        CreateProvider(handler).Should().BeAssignableTo<INotificationProvider>();
    }
}

// ── Mock HTTP infrastructure ──────────────────────────────────────────────────

file sealed class StaticTwilioAuthTokenProvider(string token) : CardVault.Api.Services.Notifications.Providers.ITwilioAuthTokenProvider
{
    public string GetAuthToken() => token;
}

file sealed class TwilioMockHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastRequestBody { get; private set; } = string.Empty;

    public TwilioMockHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        };
    }
}

file sealed class TwilioTimeoutMockHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new TaskCanceledException("Simulated Twilio timeout");
}
