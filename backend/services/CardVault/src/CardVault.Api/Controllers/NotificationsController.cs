using CardVault.Application.Features.Notifications.Queries;
using CardVault.Application.Services;
using CardVault.Api.Pci;
using CardVault.Application.Services.Notifications;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

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
        // Reset stream position so the validator can re-read the body (e.g. form-encoded Twilio)
        Request.Body.Position = 0;

        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString();

        // 3. Missing signature header → 401 + audit reason = missing-signature
        // (checked before calling Validate so the controller stays the authoritative
        // gate for this case, independent of any validator implementation detail)
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

        // 4. Validate signature — discriminated result maps to specific audit reasons
        var validationResult = validator.Validate(Request, rawBodyBytes);

        if (validationResult != WebhookValidationResult.Valid)
        {
            var auditReason = validationResult switch
            {
                WebhookValidationResult.Replayed => "replayed",
                WebhookValidationResult.MissingSignature => "missing-signature",
                _ => "invalid-signature"
            };

            await audit.WriteAsync(
                "webhook.delivery-callback.rejected",
                new { providerId, reason = auditReason },
                correlationId: null,
                traceId,
                ct);
            return Results.Unauthorized();
        }

        // 5. Parse providerReference from the JSON body (best-effort)
        string? providerReference = null;
        try
        {
            using var doc = JsonDocument.Parse(rawBodyBytes);
            if (doc.RootElement.TryGetProperty("providerReference", out var propEl))
                providerReference = propEl.GetString();
        }
        catch (JsonException) { /* malformed body — proceed without providerReference */ }

        // 6. Mark the delivery as confirmed (set DeliveredOn) if we have a reference
        CustomerNotificationDeliveryEntity? confirmedDelivery = null;
        if (!string.IsNullOrEmpty(providerReference))
        {
            confirmedDelivery = await db.CustomerNotificationDeliveries
                .FirstOrDefaultAsync(
                    d => d.ProviderReference == providerReference && d.DeliveredOn == null,
                    ct);
            if (confirmedDelivery is not null)
            {
                confirmedDelivery.DeliveredOn = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        // 7. PCI audit — delivery confirmed event (fire-and-forget safe; NullEventBus in tests)
        // Subject must be deliveryId per design §11; emit only when a delivery row was confirmed.
        if (confirmedDelivery is not null)
        {
            await pciAudit.PublishAsync(
                "pci.notification.delivery-confirmed",
                subject: confirmedDelivery.Id.ToString(),
                new
                {
                    deliveryId = confirmedDelivery.Id,
                    providerId,
                    providerReference,
                    deliveredOn = confirmedDelivery.DeliveredOn,
                    tenantId = (Guid?)null // tenantId not available at this layer; included for schema completeness
                },
                ct);
        }

        return Results.Ok();
    }
}
