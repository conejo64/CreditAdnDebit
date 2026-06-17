using System.Net;
using System.Text;
using CardVault.Application.Services.Notifications;
using CardVault.Application.Services.Notifications.Providers;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardVault.Tests.Features.Notifications.Providers;

/// <summary>
/// HTTP-mocked unit tests for <see cref="MovistarEcuadorSmsProvider"/>.
/// NEVER calls real Movistar EC endpoints.
/// All SOAP/REST responses are built locally — no external dependencies.
/// </summary>
public sealed class MovistarEcuadorSmsProviderTests
{
    private const string TestApiKey = "test-movistar-api-key-not-real";
    private const string TestBaseUrl = "https://sms.movistar.ec";

    // ── Helper factory ──────────────────────────────────────────────────────

    private static MovistarEcuadorSmsProvider CreateProvider(
        HttpMessageHandler handler,
        bool degradedConfirmation = false,
        bool useRestProtocol = false,
        string apiKey = TestApiKey)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(TestBaseUrl)
        };

        var options = Options.Create(new MovistarOptions
        {
            SenderId = "CardVault",
            DegradedConfirmation = degradedConfirmation,
            UseRestProtocol = useRestProtocol
        });

        var apiKeyProvider = new StaticMovistarApiKeyProvider(apiKey);

        return new MovistarEcuadorSmsProvider(
            httpClient,
            options,
            apiKeyProvider,
            NullLogger<MovistarEcuadorSmsProvider>.Instance);
    }

    private static NotificationSendRequest MakeRequest(string destination = "+593987654321") => new(
        DeliveryId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        Channel: NotificationChannel.Sms,
        Destination: destination,
        RenderedSubject: string.Empty,
        RenderedBody: "Your OTP is 654321",
        TemplateType: "Otp",
        Locale: "es-EC");

    // ── Response body builders ─────────────────────────────────────────────

    private static string BuildSoapSuccessResponse(string messageId) => $"""
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                          xmlns:ns="http://sms.movistar.ec/gateway/v1">
          <soapenv:Body>
            <ns:SendMessageResponse>
              <ns:MessageId>{messageId}</ns:MessageId>
              <ns:Status>ACCEPTED</ns:Status>
            </ns:SendMessageResponse>
          </soapenv:Body>
        </soapenv:Envelope>
        """;

    private static string BuildSoapFaultResponse(string faultCode, string? detailErrorCode = null)
    {
        var detail = detailErrorCode is not null
            ? $"<detail><errorCode>{detailErrorCode}</errorCode></detail>"
            : string.Empty;
        return $"""
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/">
              <soapenv:Body>
                <soapenv:Fault>
                  <faultcode>{faultCode}</faultcode>
                  <faultstring>Fault occurred</faultstring>
                  {detail}
                </soapenv:Fault>
              </soapenv:Body>
            </soapenv:Envelope>
            """;
    }

    private static string BuildSoapErrorCodeResponse(string errorCode) => $"""
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                          xmlns:ns="http://sms.movistar.ec/gateway/v1">
          <soapenv:Body>
            <ns:SendMessageResponse>
              <ns:Status>ERROR</ns:Status>
              <ns:ErrorCode>{errorCode}</ns:ErrorCode>
            </ns:SendMessageResponse>
          </soapenv:Body>
        </soapenv:Envelope>
        """;

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void ProviderId_IsMovistarEc()
    {
        var provider = CreateProvider(new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-ID")));
        provider.ProviderId.Should().Be("movistar-ec");
    }

    [Fact]
    public void Channel_IsSms()
    {
        var provider = CreateProvider(new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("x")));
        provider.Channel.Should().Be(NotificationChannel.Sms);
    }

    [Fact]
    public void MovistarEcuadorSmsProvider_ImplementsINotificationProvider()
    {
        CreateProvider(new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("x")))
            .Should().BeAssignableTo<INotificationProvider>();
    }

    // ── CanHandle ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("+593987654321", true,  "Ecuador mobile prefix must be handled by Movistar EC")]
    [InlineData("+5930987654321", true, "Ecuador number with extra digit still starts with +593")]
    [InlineData("+15550009999",   false, "US prefix must NOT be handled by Movistar EC")]
    [InlineData("+441234567890",  false, "UK prefix must NOT be handled")]
    [InlineData("+34612345678",   false, "Spain prefix must NOT be handled")]
    public void CanHandle_ReturnsExpectedResult(string destination, bool expected, string reason)
    {
        var provider = CreateProvider(new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("x")));
        provider.CanHandle(destination).Should().Be(expected, reason);
    }

    // ── Happy SOAP path ──────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_HappySoapPath_ReturnsAcceptedWithMessageIdAsProviderReference()
    {
        var messageId = "MSG-MOVISTAR-12345";
        var provider = CreateProvider(
            new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse(messageId)));

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().Be(messageId);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_HappySoapPath_PostsWithXmlContentType()
    {
        var handler = new MovistarCapturingHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-99"));
        var provider = CreateProvider(handler);

        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastContentType.Should().Contain("xml",
            "SOAP requests must be sent with an XML content type (text/xml or application/soap+xml)");
    }

    [Fact]
    public async Task SendAsync_HappySoapPath_PostsHttpPost()
    {
        var handler = new MovistarCapturingHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-100"));
        var provider = CreateProvider(handler);

        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_HappySoapPath_BodyContainsDestinationNumber()
    {
        var handler = new MovistarCapturingHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-200"));
        var provider = CreateProvider(handler);
        var request = MakeRequest("+593911111111");

        await provider.SendAsync(request, CancellationToken.None);

        handler.LastRequestBody.Should().Contain("+593911111111",
            "the SOAP body must include the destination phone number");
    }

    // ── SOAP fault soap:Server → TransientFailure ─────────────────────────

    [Fact]
    public async Task SendAsync_SoapServerFault_ReturnsTransientFailure()
    {
        // soap:Server fault with no specific error code — faultcode alone classifies it
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.InternalServerError,
            BuildSoapFaultResponse("soap:Server"));

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "soap:Server faults are server-side errors — transient per design §6");
    }

    // ── SOAP fault soap:Client → PermanentFailure ─────────────────────────

    [Fact]
    public async Task SendAsync_SoapClientFault_ReturnsPermanentFailure()
    {
        // soap:Client fault with no specific error code
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.BadRequest,
            BuildSoapFaultResponse("soap:Client"));

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure,
            "soap:Client faults indicate a bad/invalid request — permanent per design §6");
    }

    // ── Permanent error codes ──────────────────────────────────────────────

    [Theory]
    [InlineData("INVALID_MSISDN",  "invalid destination number — permanent")]
    [InlineData("BLACKLISTED",     "number is blacklisted — permanent")]
    [InlineData("AUTH_FAILED",     "authentication failed — permanent for this delivery")]
    public async Task SendAsync_PermanentErrorCode_ReturnsPermanentFailure(string errorCode, string reason)
    {
        // Error codes in fault detail with soap:Client (consistent classification)
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.BadRequest,
            BuildSoapFaultResponse("soap:Client", errorCode));

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure, reason);
        result.ErrorCode.Should().Be(errorCode);
    }

    // ── Transient error codes ──────────────────────────────────────────────

    [Theory]
    [InlineData("SYSTEM_BUSY", "gateway overloaded — transient, retry later")]
    [InlineData("THROTTLED",   "rate throttled — transient, retry later")]
    public async Task SendAsync_TransientErrorCode_ReturnsTransientFailure(string errorCode, string reason)
    {
        // Transient error codes in a 5xx SOAP response
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.ServiceUnavailable,
            BuildSoapErrorCodeResponse(errorCode));

        var provider = CreateProvider(handler);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure, reason);
    }

    // ── HTTP 429 → TransientFailure ───────────────────────────────────────

    [Fact]
    public async Task SendAsync_RateLimited429_ReturnsTransientFailure()
    {
        var provider = CreateProvider(
            new MovistarFixedResponseHandler(HttpStatusCode.TooManyRequests, "<error>Rate limited</error>"));

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "429 is transient per ADR-3 — must be retried");
    }

    // ── HTTP 5xx → TransientFailure ───────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task SendAsync_ServerError5xx_ReturnsTransientFailure(int statusCode)
    {
        var provider = CreateProvider(
            new MovistarFixedResponseHandler((HttpStatusCode)statusCode, "<error>Server error</error>"));

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            $"HTTP {statusCode} from Movistar EC is a server error — transient");
    }

    // ── Network timeout → TransientFailure ───────────────────────────────

    [Fact]
    public async Task SendAsync_NetworkTimeout_ReturnsTransientFailure()
    {
        var provider = CreateProvider(new MovistarTimeoutHandler());

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure,
            "network timeouts are transient and must be retried");
        result.ErrorCode.Should().NotBeNullOrEmpty("error code must be set to identify the timeout");
    }

    // ── Degraded confirmation (DegradedConfirmation = true) ──────────────

    [Fact]
    public async Task SendAsync_DegradedConfirmationEnabled_AcceptedResultHasProviderReportedAtSet()
    {
        // Degraded mode: Movistar has no DLR callback, so accepted = confirmed delivery
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var provider = CreateProvider(
            new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-DEGRADED")),
            degradedConfirmation: true);

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReportedAt.Should().NotBeNull(
            "degraded mode signals delivery confirmation at send time via ProviderReportedAt");
        result.ProviderReportedAt!.Value.Should().BeOnOrAfter(before,
            "ProviderReportedAt must reflect the time of the synchronous send");
    }

    [Fact]
    public async Task SendAsync_DegradedConfirmationDisabled_AcceptedResultHasNullProviderReportedAt()
    {
        // Normal mode: Movistar delivers a DLR callback, no need to pre-confirm
        var provider = CreateProvider(
            new MovistarFixedResponseHandler(HttpStatusCode.OK, BuildSoapSuccessResponse("MSG-NORMAL")),
            degradedConfirmation: false);

        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReportedAt.Should().BeNull(
            "normal mode (with DLR) does not pre-confirm delivery — webhook sets DeliveredOn");
    }

    // ── REST protocol (UseRestProtocol = true) ────────────────────────────

    [Fact]
    public async Task SendAsync_RestProtocol_HappyPath_ReturnsAcceptedWithMessageId()
    {
        var messageId = "REST-MSG-5678";
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.OK,
            $$"""{"messageId":"{{messageId}}","status":"ACCEPTED"}""");

        var provider = CreateProvider(handler, useRestProtocol: true);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().Be(messageId);
    }

    [Fact]
    public async Task SendAsync_RestProtocol_PermanentErrorCode_ReturnsPermanentFailure()
    {
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.BadRequest,
            """{"errorCode":"INVALID_MSISDN","message":"Invalid phone number"}""");

        var provider = CreateProvider(handler, useRestProtocol: true);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure);
        result.ErrorCode.Should().Be("INVALID_MSISDN");
    }

    [Fact]
    public async Task SendAsync_RestProtocol_ServerError5xx_ReturnsTransientFailure()
    {
        var handler = new MovistarFixedResponseHandler(
            HttpStatusCode.InternalServerError,
            """{"errorCode":"SYSTEM_BUSY","message":"Gateway overloaded"}""");

        var provider = CreateProvider(handler, useRestProtocol: true);
        var result = await provider.SendAsync(MakeRequest(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure);
    }

    [Fact]
    public async Task SendAsync_RestProtocol_PostsJsonContentType()
    {
        var handler = new MovistarCapturingHandler(
            HttpStatusCode.OK,
            """{"messageId":"REST-999","status":"ACCEPTED"}""");

        var provider = CreateProvider(handler, useRestProtocol: true);
        await provider.SendAsync(MakeRequest(), CancellationToken.None);

        handler.LastContentType.Should().Contain("json",
            "REST requests must use application/json content type");
    }
}

// ── Mock HTTP infrastructure ───────────────────────────────────────────────

file sealed class StaticMovistarApiKeyProvider(string apiKey) : IMovistarApiKeyProvider
{
    public string GetApiKey() => apiKey;
}

/// <summary>Returns a fixed status code + body for every request.</summary>
file sealed class MovistarFixedResponseHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var contentType = responseBody.TrimStart().StartsWith('<') ? "text/xml" : "application/json";
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, contentType)
        });
    }
}

/// <summary>Captures the last request for assertion, returns a configured response.</summary>
file sealed class MovistarCapturingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastRequestBody { get; private set; } = string.Empty;
    public string? LastContentType { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastContentType = request.Content?.Headers.ContentType?.ToString();
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        var contentType = responseBody.TrimStart().StartsWith('<') ? "text/xml" : "application/json";
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, contentType)
        };
    }
}

/// <summary>Always throws <see cref="TaskCanceledException"/> to simulate a network timeout.</summary>
file sealed class MovistarTimeoutHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => throw new TaskCanceledException("Simulated Movistar EC network timeout");
}
