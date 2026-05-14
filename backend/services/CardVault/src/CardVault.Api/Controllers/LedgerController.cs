using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Features.Ledger.Commands;
using CardVault.Api.Features.Ledger.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/ledger")]
[Authorize(Policy = "CanViewLedger")]
public class LedgerController : ControllerBase
{
    private readonly IMediator _mediator;

    public LedgerController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("purchase")]
    [Authorize(Policy = "CanOperateLedger")]
    public async Task<IResult> Purchase([FromBody] PostLedgerRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new PostLedgerCommand(req, LedgerEntryType.Purchase), ct);
    }

    [HttpPost("payment")]
    [Authorize(Policy = "CanOperateLedger")]
    public async Task<IResult> Payment([FromBody] PostLedgerRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new PostLedgerCommand(req, LedgerEntryType.Payment), ct);
    }

    [HttpPost("fee")]
    [Authorize(Policy = "CanOperateLedger")]
    public async Task<IResult> Fee([FromBody] PostLedgerRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new PostLedgerCommand(req, LedgerEntryType.Fee), ct);
    }

    [HttpPost("interest")]
    [Authorize(Policy = "CanOperateLedger")]
    public async Task<IResult> Interest([FromBody] PostLedgerRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new PostLedgerCommand(req, LedgerEntryType.Interest), ct);
    }

    [HttpGet("accounts/{accountId:guid}/balance")]
    public async Task<IResult> Balance(Guid accountId, CancellationToken ct)
    {
        return await _mediator.Send(new GetBalanceQuery(accountId), ct);
    }

    [HttpGet("accounts/{accountId:guid}/movements")]
    public async Task<IResult> Movements(Guid accountId, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetMovementsQuery(accountId, take), ct);
    }
}
