using CardVault.Application.Contracts;
using CardVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/credit-limits")]
[Authorize(Policy = "CanViewCreditLimits")]
public class CreditLimitManagementController : ControllerBase
{
    private readonly CreditLimitManagementService _service;

    public CreditLimitManagementController(CreditLimitManagementService service)
    {
        _service = service;
    }

    [HttpGet("accounts/{accountId:guid}/overlimit-events")]
    public async Task<ActionResult<IReadOnlyList<OverlimitEventView>>> GetOverlimitEvents(Guid accountId, [FromQuery] int take, CancellationToken ct)
        => Ok(await _service.GetOverlimitEventsAsync(accountId, take, ct));

    [HttpPost("accounts/{accountId:guid}/evaluate")]
    [Authorize(Policy = "CanManageCreditLimits")]
    public async Task<ActionResult<CreditLimitEvaluationView>> Evaluate(Guid accountId, CancellationToken ct)
        => Ok(await _service.EvaluateAsync(accountId, ct));

    [HttpGet("proposals")]
    public async Task<ActionResult<IReadOnlyList<CreditLimitProposalView>>> GetProposals([FromQuery] string? status, [FromQuery] int take, CancellationToken ct)
        => Ok(await _service.GetProposalsAsync(status, take, ct));

    [HttpPost("proposals/{proposalId:guid}/apply")]
    [Authorize(Policy = "CanManageCreditLimits")]
    public async Task<ActionResult<CreditLimitProposalView>> Apply(Guid proposalId, CancellationToken ct)
        => Ok(await _service.ApplyProposalAsync(proposalId, ct));
}
