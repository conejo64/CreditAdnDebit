using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Features.Issuer.Commands;
using CardVault.Api.Features.Issuer.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/issuer")]
[Authorize(Policy = "CanOperateIssuer")]
public class IssuerController : ControllerBase
{
    private readonly IMediator _mediator;

    public IssuerController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("customers")]
    public async Task<IResult> CreateCustomer([FromBody] CreateCustomerRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new CreateCustomerCommand(req), ct);
    }

    [HttpGet("customers/{id:guid}")]
    public async Task<IResult> GetCustomer(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetCustomerQuery(id), ct);
    }

    [HttpGet("customers")]
    public async Task<IResult> SearchCustomers([FromQuery] string? q, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new SearchCustomersQuery(q, take), ct);
    }

    [HttpPost("accounts")]
    public async Task<IResult> CreateAccount([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new IssueAccountCommand(req), ct);
    }

    [HttpGet("accounts")]
    public async Task<IResult> GetAccounts([FromQuery] string? q, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetAccountsQuery(q, take), ct);
    }

    [HttpGet("accounts/{accountId:guid}/limits")]
    public async Task<IResult> GetAccountLimits(Guid accountId, CancellationToken ct)
    {
        return await _mediator.Send(new GetAccountLimitsQuery(accountId), ct);
    }

    [HttpPut("accounts/{accountId:guid}/limits")]
    public async Task<IResult> UpdateAccountLimits(Guid accountId, [FromBody] AccountLimitEntity limits, CancellationToken ct)
    {
        limits.AccountId = accountId;
        return await _mediator.Send(new UpdateAccountLimitsCommand(limits), ct);
    }

    [HttpGet("cards")]
    public async Task<IResult> GetCards([FromQuery] string? q, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetCardsQuery(q, take), ct);
    }

    [HttpPost("cards/issue")]
    public async Task<IResult> IssueCard([FromBody] IssueCardRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new IssueCardCommand(req), ct);
    }

    [HttpGet("cards/{id:guid}")]
    public async Task<IResult> GetCard(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetCardQuery(id), ct);
    }

    [HttpPost("cards/{id:guid}/activate")]
    public async Task<IResult> ActivateCard(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new ActivateCardCommand(id), ct);
    }

    [HttpPost("cards/{id:guid}/block")]
    public async Task<IResult> BlockCard(Guid id, [FromBody] BlockCardRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new BlockCardCommand(id, req), ct);
    }

    [HttpPost("cards/{id:guid}/unblock")]
    public async Task<IResult> UnblockCard(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new UnblockCardCommand(id), ct);
    }

    [HttpPost("cards/{id:guid}/cancel")]
    public async Task<IResult> CancelCard(Guid id, [FromBody] CancelCardRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new CancelCardCommand(id, req), ct);
    }

    [HttpPost("cards/{id:guid}/replace")]
    public async Task<IResult> ReplaceCard(Guid id, [FromBody] ReplaceCardRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new ReplaceCardCommand(id, req), ct);
    }

    [HttpPost("cards/{id:guid}/pin")]
    public async Task<IResult> SetPin(Guid id, [FromBody] SetPinRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new SetPinCommand(id, req), ct);
    }
}

public record SetPinRequest(string Pin);

[ApiController]
[Route("api/credit/policies")]
[Authorize(Policy = "CanManageCreditPolicies")]
public class CreditPoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CreditPoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPut]
    public async Task<IResult> UpsertPolicy([FromBody] CreditPolicyEntity req, CancellationToken ct)
    {
        return await _mediator.Send(new UpsertCreditPolicyCommand(req), ct);
    }

    [HttpGet("{productCode}")]
    public async Task<IResult> GetPolicy(string productCode, CancellationToken ct)
    {
        return await _mediator.Send(new GetCreditPolicyQuery(productCode), ct);
    }
}
