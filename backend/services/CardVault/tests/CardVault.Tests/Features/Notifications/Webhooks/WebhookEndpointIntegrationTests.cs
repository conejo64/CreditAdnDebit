using System.Net;
using System.Text;
using System.Text.Json;
using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Notifications.Webhooks;

/// <summary>
/// Integration tests for <c>POST /api/notifications/delivery-callback/{providerId}</c>.
///
/// All tests inject <see cref="FakeWebhookSignatureValidator"/> instances via
/// <see cref="WebApplicationFactory{T}.WithWebHostBuilder"/> so that the
/// cryptographic validation (unit-tested in 1e.1) is bypassed here.
/// This class focuses on endpoint behaviour: routing, validation ordering,
/// audit events, DB mutations, and rate-limiting.
/// </summary>
[Collection("WebApp")]
public sealed class WebhookEndpointIntegrationTests : IClassFixture<CardVaultWebApplicationFactory>
{
    private readonly CardVaultWebApplicationFactory _factory;
    private const string BasePath = "/api/notifications/delivery-callback";

    public WebhookEndpointIntegrationTests(CardVaultWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a factory where all three known providers have fake validators
    /// that return <paramref name="validatorResult"/>.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactoryWithFakes(bool validatorResult = true)
        => _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            // Register fake validators last; they shadow any real ones registered by Program.cs.
            services.AddKeyedSingleton<IWebhookSignatureValidator>("twilio",
                new FakeWebhookSignatureValidator("twilio", validatorResult));
            services.AddKeyedSingleton<IWebhookSignatureValidator>("sendgrid",
                new FakeWebhookSignatureValidator("sendgrid", validatorResult));
            services.AddKeyedSingleton<IWebhookSignatureValidator>("movistar-ec",
                new FakeWebhookSignatureValidator("movistar-ec", validatorResult));
        }));

    /// <summary>
    /// Builds a <c>POST</c> request to the webhook callback endpoint.
    /// When <paramref name="includeSignatureHeader"/> is <c>true</c> (default),
    /// the <see cref="FakeWebhookSignatureValidator.FakeSignatureHeaderName"/> header
    /// is added so the controller's missing-header check passes.
    /// </summary>
    private static HttpRequestMessage BuildWebhookRequest(
        string providerId,
        string? jsonBody = null,
        bool includeSignatureHeader = true)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BasePath}/{providerId}");

        req.Content = new StringContent(
            jsonBody ?? "{}",
            Encoding.UTF8,
            "application/json");

        if (includeSignatureHeader)
            req.Headers.Add(FakeWebhookSignatureValidator.FakeSignatureHeaderName, "test-value");

        return req;
    }

    /// <summary>Adds a <c>CustomerNotificationDeliveryEntity</c> (with parent notification) to the DB.</summary>
    private static async Task<CustomerNotificationDeliveryEntity> SeedDeliveryAsync(
        CardVaultDbContext db,
        string providerReference,
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Sent)
    {
        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Type = CustomerNotificationType.Transaction,
            Title = "Test notification",
            Message = "Test message body",
            CreatedOn = DateTimeOffset.UtcNow
        };

        var delivery = new CustomerNotificationDeliveryEntity
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            Channel = NotificationChannel.Email,
            DestinationMasked = "t***@example.com",
            DestinationHash = "sha256-placeholder",
            Status = status,
            ProviderReference = providerReference,
            Attempts = 1,
            CreatedOn = DateTimeOffset.UtcNow
        };

        notification.Deliveries.Add(delivery);
        db.CustomerNotifications.Add(notification);
        await db.SaveChangesAsync();
        return delivery;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §1 — Happy-path: valid callbacks update DeliveredOn and return 200
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidTwilioCallback_Returns200_AndSetsDeliveredOn()
    {
        using var factory = CreateFactoryWithFakes(validatorResult: true);
        var providerRef = $"SM-twilio-{Guid.NewGuid():N}";

        // Seed
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
            await SeedDeliveryAsync(db, providerRef);
        }

        // Act
        var client = factory.CreateClient();
        var body = JsonSerializer.Serialize(new { providerReference = providerRef });
        var req = BuildWebhookRequest("twilio", body);
        var response = await client.SendAsync(req);

        // Assert — HTTP 200
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a valid Twilio signature must be accepted and return 200");

        // Assert — DeliveredOn updated in DB
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var updated = verifyDb.CustomerNotificationDeliveries
            .FirstOrDefault(d => d.ProviderReference == providerRef);

        updated.Should().NotBeNull();
        updated!.DeliveredOn.Should().NotBeNull(
            because: "a valid delivery callback must set DeliveredOn on the Sent delivery");
    }

    [Fact]
    public async Task ValidSendGridCallback_Returns200()
    {
        using var factory = CreateFactoryWithFakes(validatorResult: true);

        var client = factory.CreateClient();
        var req = BuildWebhookRequest("sendgrid", "{}");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a valid SendGrid signature must return 200");
    }

    [Fact]
    public async Task ValidMovistarCallback_Returns200()
    {
        using var factory = CreateFactoryWithFakes(validatorResult: true);

        var client = factory.CreateClient();
        var req = BuildWebhookRequest("movistar-ec", "{}");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a valid Movistar-EC signature must return 200");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §2 — Security: missing or tampered signatures are rejected
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingSignatureHeader_Returns401_WithMissingSignatureAuditEvent()
    {
        using var factory = CreateFactoryWithFakes(validatorResult: true); // result doesn't matter; header absent
        var providerRef = $"SM-missing-{Guid.NewGuid():N}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
            await SeedDeliveryAsync(db, providerRef);
        }

        // Request WITHOUT signature header
        var client = factory.CreateClient();
        var body = JsonSerializer.Serialize(new { providerReference = providerRef });
        var req = BuildWebhookRequest("twilio", body, includeSignatureHeader: false);
        var response = await client.SendAsync(req);

        // Assert — HTTP 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "missing signature header must be rejected with 401");

        // Assert — audit event written with reason = "missing-signature"
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var auditEvent = verifyDb.AuditEvents
            .OrderByDescending(e => e.OccurredOn)
            .FirstOrDefault(e => e.EventType == "webhook.delivery-callback.rejected");

        auditEvent.Should().NotBeNull(because: "a rejection must produce an audit event");
        auditEvent!.PayloadJson.Should().Contain("missing-signature",
            because: "the audit reason must be 'missing-signature' when the header is absent");

        // Assert — NO DB write to delivery row
        var delivery = verifyDb.CustomerNotificationDeliveries
            .FirstOrDefault(d => d.ProviderReference == providerRef);
        delivery.Should().NotBeNull();
        delivery!.DeliveredOn.Should().BeNull(
            because: "a rejected callback must not touch DeliveredOn");
    }

    [Fact]
    public async Task TamperedSignature_Returns401_WithInvalidSignatureAuditEvent()
    {
        using var factory = CreateFactoryWithFakes(validatorResult: false); // fake always rejects
        var providerRef = $"SM-tampered-{Guid.NewGuid():N}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
            await SeedDeliveryAsync(db, providerRef);
        }

        // Request WITH signature header present, but fake validator returns false
        var client = factory.CreateClient();
        var body = JsonSerializer.Serialize(new { providerReference = providerRef });
        var req = BuildWebhookRequest("twilio", body, includeSignatureHeader: true);
        var response = await client.SendAsync(req);

        // Assert — HTTP 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "a tampered (invalid) signature must be rejected with 401");

        // Assert — audit event with reason = "invalid-signature"
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CardVaultDbContext>();
        var auditEvent = verifyDb.AuditEvents
            .OrderByDescending(e => e.OccurredOn)
            .FirstOrDefault(e => e.EventType == "webhook.delivery-callback.rejected");

        auditEvent.Should().NotBeNull(because: "a rejection must produce an audit event");
        auditEvent!.PayloadJson.Should().Contain("invalid-signature",
            because: "the audit reason must be 'invalid-signature' when the header is present but invalid");

        // Assert — NO DB write to delivery row
        var delivery = verifyDb.CustomerNotificationDeliveries
            .FirstOrDefault(d => d.ProviderReference == providerRef);
        delivery.Should().NotBeNull();
        delivery!.DeliveredOn.Should().BeNull(
            because: "a rejected callback must not touch DeliveredOn");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §3 — Routing: unknown providerId must return 404
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnknownProviderId_Returns404()
    {
        // Use the base factory — no fake registered for "unknown-provider"
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            // Register fakes for known providers but NOT for "unknown-provider"
            services.AddKeyedSingleton<IWebhookSignatureValidator>("twilio",
                new FakeWebhookSignatureValidator("twilio"));
            services.AddKeyedSingleton<IWebhookSignatureValidator>("sendgrid",
                new FakeWebhookSignatureValidator("sendgrid"));
            services.AddKeyedSingleton<IWebhookSignatureValidator>("movistar-ec",
                new FakeWebhookSignatureValidator("movistar-ec"));
        }));

        var client = factory.CreateClient();
        var req = BuildWebhookRequest("unknown-provider", "{}");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            because: "an unregistered providerId must return 404");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // §4 — Rate limiting: exceeding the per-provider limit returns 429
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RateLimitExceeded_Twilio_Returns429OnSecondRequest()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
        {
            // Override rate-limit for twilio to 1 request/minute
            b.UseSetting("Notifications:Webhook:RateLimits:Twilio", "1");
            b.ConfigureTestServices(services =>
            {
                services.AddKeyedSingleton<IWebhookSignatureValidator>("twilio",
                    new FakeWebhookSignatureValidator("twilio"));
                services.AddKeyedSingleton<IWebhookSignatureValidator>("sendgrid",
                    new FakeWebhookSignatureValidator("sendgrid"));
                services.AddKeyedSingleton<IWebhookSignatureValidator>("movistar-ec",
                    new FakeWebhookSignatureValidator("movistar-ec"));
            });
        });

        var client = factory.CreateClient();

        // First request — must succeed (within limit)
        var firstReq = BuildWebhookRequest("twilio", "{}");
        var firstResponse = await client.SendAsync(firstReq);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the first request is within the rate limit");

        // Second request — must be throttled
        var secondReq = BuildWebhookRequest("twilio", "{}");
        var secondResponse = await client.SendAsync(secondReq);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            because: "the second request exceeds the per-provider rate limit");
    }
}
