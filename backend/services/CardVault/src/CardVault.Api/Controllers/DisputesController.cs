using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Api.Features.Disputes.Commands;
using CardVault.Api.Features.Disputes.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/disputes")]
[Authorize(Policy = "CanViewDisputes")]
public class DisputesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DisputesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("accounts/{accountId:guid}")]
    public async Task<IResult> GetDisputes(Guid accountId, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetDisputesQuery(accountId, take), ct);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "CanManageDisputes")]
    public async Task<IResult> CloseDispute(Guid id, [FromQuery] bool won, CancellationToken ct)
    {
        return await _mediator.Send(new CloseDisputeCommand(id, won), ct);
    }

    [HttpPost("{id:guid}/transition")]
    [Authorize(Policy = "CanManageDisputes")]
    public async Task<IResult> TransitionDispute(Guid id, [FromBody] DisputeTransitionRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new TransitionDisputeCommand(id, req), ct);
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IResult> GetDisputeEvents(Guid id, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetDisputeEventsQuery(id, take), ct);
    }
}
