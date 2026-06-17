using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Application.Features.Settlement.Commands;
using CardVault.Application.Features.Settlement.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/settlement")]
[Authorize(Policy = "CanViewSettlement")]
public class SettlementController : ControllerBase
{
    private readonly IMediator _mediator;

    public SettlementController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("run")]
    [Authorize(Policy = "CanRunSettlement")]
    public async Task<IResult> RunSettlement([FromQuery] string network, [FromQuery] DateOnly businessDate, CancellationToken ct)
    {
        return await _mediator.Send(new RunSettlementCommand(network, businessDate), ct);
    }

    [HttpGet("batches")]
    public async Task<IResult> GetBatches([FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetSettlementBatchesQuery(take), ct);
    }

    [HttpGet("batches/{id:guid}")]
    public async Task<IResult> GetBatch(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetSettlementBatchQuery(id), ct);
    }
}

[ApiController]
[Route("api/switch")]
[Authorize(Policy = "CanViewSwitchMonitor")]
public class SwitchController : ControllerBase
{
    private readonly IMediator _mediator;

    public SwitchController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("journal")]
    public async Task<IResult> GetJournal([FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetSwitchJournalQuery(take), ct);
    }
}

[ApiController]
[Route("api/reconciliation")]
[Authorize(Policy = "CanViewSettlement")]
public class ReconciliationController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReconciliationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("settlement/{batchId:guid}")]
    public async Task<IResult> ReconcileSettlement(Guid batchId, CancellationToken ct)
    {
        return await _mediator.Send(new ReconcileSettlementQuery(batchId), ct);
    }
}

[ApiController]
[Route("api/holds")]
[Authorize(Policy = "CanOperateBilling")]
public class HoldsController : ControllerBase
{
    private readonly IMediator _mediator;

    public HoldsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("expire/run")]
    public async Task<IResult> ExpireHolds(CancellationToken ct)
    {
        return await _mediator.Send(new ExpireHoldsCommand(), ct);
    }
}
