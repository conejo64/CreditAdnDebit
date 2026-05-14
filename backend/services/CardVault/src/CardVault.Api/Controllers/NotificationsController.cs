using CardVault.Api.Features.Notifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}
