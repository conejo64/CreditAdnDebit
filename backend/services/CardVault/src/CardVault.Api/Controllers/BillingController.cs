using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Application.Contracts;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Application.Features.Billing.Commands;
using CardVault.Application.Features.Billing.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize(Policy = "CanViewBilling")]
public class BillingController : ControllerBase
{
    private readonly IMediator _mediator;

    public BillingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("statements/generate")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> GenerateStatement([FromBody] GenerateStatementRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new GenerateStatementCommand(req), ct);
    }

    [HttpGet("statements/{id:guid}")]
    public async Task<IResult> GetStatement(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetStatementQuery(id), ct);
    }

    [HttpGet("accounts/{accountId:guid}/statements")]
    public async Task<IResult> GetStatementsForAccount(Guid accountId, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetStatementsByAccountQuery(accountId, take), ct);
    }

    [HttpPost("statements/{id:guid}/pay")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> ApplyPayment(Guid id, [FromBody] ApplyPaymentRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new ApplyPaymentCommand(id, req), ct);
    }

    [HttpGet("statements/{id:guid}/buckets")]
    public async Task<IResult> GetStatementBuckets(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetStatementBucketsQuery(id), ct);
    }

    [HttpPost("statements/{id:guid}/latefee")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> ApplyLateFee(Guid id, [FromQuery] bool force, CancellationToken ct)
    {
        return await _mediator.Send(new ApplyLateFeeCommand(id, force), ct);
    }

    [HttpPost("latefees/run")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> RunLateFees([FromQuery] bool force, CancellationToken ct)
    {
        return await _mediator.Send(new RunLateFeesCommand(force), ct);
    }

    [HttpPost("statements/{id:guid}/recalculate")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> RecalculateStatement(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new RecalculateStatementCommand(id), ct);
    }

    [HttpGet("statements/{id:guid}/print")]
    public async Task<IResult> PrintStatement(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new PrintStatementQuery(id), ct);
    }

    [HttpGet("statements/{id:guid}/pdf")]
    public async Task<IResult> GetStatementPdf(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetStatementPdfQuery(id), ct);
    }

    [HttpGet("policies/minimum-payment")]
    public async Task<IResult> GetMinimumPaymentPolicies(CancellationToken ct)
    {
        return await _mediator.Send(new GetMinimumPaymentPoliciesQuery(), ct);
    }

    [HttpPost("policies/minimum-payment")]
    [Authorize(Policy = "CanManageBillingPolicies")]
    public async Task<IResult> UpsertMinimumPaymentPolicy([FromBody] MinimumPaymentPolicyUpsert req, CancellationToken ct)
    {
        return await _mediator.Send(new UpsertMinimumPaymentPolicyCommand(req), ct);
    }

    [HttpPost("installments/defer")]
    [Authorize(Policy = "CanOperateBilling")]
    public async Task<IResult> DeferPurchase([FromBody] DeferPurchaseRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new DeferPurchaseCommand(req), ct);
    }

    [HttpGet("accounts/{accountId:guid}/installments")]
    public async Task<IResult> GetActiveInstallments(Guid accountId, CancellationToken ct)
    {
        return await _mediator.Send(new GetActiveInstallmentPlansQuery(accountId), ct);
    }
}

// DeferPurchaseRequest moved to CardVault.Application.Contracts
