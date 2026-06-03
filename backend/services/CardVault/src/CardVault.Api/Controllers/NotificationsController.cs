using CardVault.Api.Features.Notifications.Queries;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "CanViewAudit")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IResult> List([FromQuery] Guid? customerId, [FromQuery] Guid? accountId, [FromQuery] string? type, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new ListCustomerNotificationsQuery(customerId, accountId, type, take), ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<IResult> Get(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetCustomerNotificationQuery(id), ct);
    }

    /// <summary>
    /// Receives a delivery status callback from a notification provider (Twilio, SendGrid, Movistar-EC).
    /// The endpoint is anonymous (providers POST without a JWT) but every request must carry a valid
    /// provider-specific HMAC or ECDSA signature.  Requests with a missing or invalid signature are
    /// rejected with 401 and an audit event is written for traceability.
    /// </summary>
    [HttpPost("delivery-callback/{providerId}")]
    [AllowAnonymous]
    [EnableRateLimiting("notifications_webhook")]
    public async Task<IResult> DeliveryCallback(
        string providerId,
        [FromServices] CardVaultDbContext db,
        [FromServices] AuditService audit,
        [FromServices] PciAuditPublisher pciAudit,
        CancellationToken ct)
    {
        // 1. Resolve validator — unknown providerId → 404
        var validator = HttpContext.RequestServices
            .GetKeyedService<IWebhookSignatureValidator>(providerId);
        if (validator is null)
            return Results.NotFound(new { error = "unknown-provider", providerId });

        // 2. Read raw body (buffered so Validate() can also inspect it)
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var rawBodyBytes = ms.ToArray();

        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString();

        // 3. Missing signature header → 401 + audit reason = missing-signature
        if (!Request.Headers.ContainsKey(validator.SignatureHeaderName))
        {
            await audit.WriteAsync(
                "webhook.delivery-callback.rejected",
                new { providerId, reason = "missing-signature" },
                correlationId: null,
                traceId,
                ct);
            return Results.Unauthorized();
        }

        // 4. Invalid signature → 401 + audit reason = invalid-signature
        if (!validator.Validate(Request, rawBodyBytes))
        {
            await audit.WriteAsync(
                "webhook.delivery-callback.rejected",
                new { providerId, reason = "invalid-signature" },
                correlationId: null,
                traceId,
                ct);
            return Results.Unauthorized();
        }

        // 5. Parse providerReference from the JSON body (best-effort)
        string? providerReference = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBodyBytes);
            if (doc.RootElement.TryGetProperty("providerReference", out var propEl))
                providerReference = propEl.GetString();
        }
        catch { /* malformed body — proceed without providerReference */ }

        // 6. Mark the delivery as confirmed (set DeliveredOn) if we have a reference
        if (!string.IsNullOrEmpty(providerReference))
        {
            var delivery = await db.CustomerNotificationDeliveries
                .FirstOrDefaultAsync(
                    d => d.ProviderReference == providerReference && d.DeliveredOn == null,
                    ct);
            if (delivery is not null)
            {
                delivery.DeliveredOn = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        // 7. PCI audit — delivery confirmed event (fire-and-forget safe; NullEventBus in tests)
        await pciAudit.PublishAsync(
            "pci.notification.delivery-confirmed",
            subject: providerId,
            new { providerId, providerReference },
            ct);

        return Results.Ok();
    }
}
