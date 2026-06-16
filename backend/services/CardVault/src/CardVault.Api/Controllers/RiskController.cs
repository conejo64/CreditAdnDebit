using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Features.Risk.Commands;
using CardVault.Api.Features.Risk.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/risk")]
[Authorize(Policy = "CanManageRisk")]
public class RiskController : ControllerBase
{
    private readonly IMediator _mediator;

    public RiskController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("mcc-rules")]
    public async Task<IResult> GetMccRules(CancellationToken ct)
    {
        return await _mediator.Send(new GetMccRulesQuery(), ct);
    }

    [HttpPost("mcc-rules")]
    public async Task<IResult> UpsertMccRule([FromBody] MccRuleUpsertRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new UpsertMccRuleCommand(req), ct);
    }

    [HttpDelete("mcc-rules/{mcc}")]
    public async Task<IResult> DeleteMccRule(string mcc, CancellationToken ct)
    {
        return await _mediator.Send(new DeleteMccRuleCommand(mcc), ct);
    }

    [HttpGet("velocity-rules")]
    public async Task<IResult> GetVelocityRules([FromQuery] string productCode, CancellationToken ct)
    {
        return await _mediator.Send(new GetVelocityRulesQuery(productCode), ct);
    }

    [HttpPost("velocity-rules")]
    public async Task<IResult> UpsertVelocityRule([FromBody] VelocityRuleUpsertRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new UpsertVelocityRuleCommand(req), ct);
    }

    [HttpDelete("velocity-rules/{id:guid}")]
    public async Task<IResult> DeleteVelocityRule(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new DeleteVelocityRuleCommand(id), ct);
    }
}

[ApiController]
[Route("api")]
[Authorize(Policy = "CanOperateBilling")]
public class FeesAndInterestController : ControllerBase
{
    private readonly IMediator _mediator;

    public FeesAndInterestController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("fees/overlimit")]
    public async Task<IResult> AssessOverlimit([FromQuery] Guid accountId, [FromQuery] DateOnly businessDate, [FromQuery] DateTimeOffset? postedOn, CancellationToken ct)
    {
        return await _mediator.Send(new AssessOverlimitFeeCommand(accountId, businessDate, postedOn), ct);
    }

    [HttpPost("fees/annual")]
    public async Task<IResult> AssessAnnual([FromQuery] Guid accountId, [FromQuery] DateOnly businessDate, [FromQuery] DateTimeOffset? postedOn, CancellationToken ct)
    {
        return await _mediator.Send(new AssessAnnualFeeCommand(accountId, businessDate, postedOn), ct);
    }

    [HttpPost("fees/cash-advance")]
    public async Task<IResult> AssessCashAdvance([FromQuery] Guid accountId, [FromQuery] decimal cashAmount, [FromQuery] DateOnly businessDate, [FromQuery] DateTimeOffset? postedOn, CancellationToken ct)
    {
        return await _mediator.Send(new AssessCashAdvanceFeeCommand(accountId, cashAmount, businessDate, postedOn), ct);
    }

    [HttpPost("interest/accrue")]
    public async Task<IResult> AccrueInterest([FromQuery] Guid accountId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        return await _mediator.Send(new AccrueInterestCommand(accountId, from, to), ct);
    }

    [HttpGet("interest/accruals")]
    public async Task<IResult> GetInterestAccruals([FromQuery] Guid accountId, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetInterestAccrualsQuery(accountId, take), ct);
    }
}
