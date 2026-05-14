using System.Security.Claims;
using CardVault.Api.Contracts;
using CardVault.Api.Features.Ecommerce3ds.Commands;
using CardVault.Api.Features.Ecommerce3ds.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/ecommerce/3ds")]
[Authorize]
public class EcommerceThreeDsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EcommerceThreeDsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("challenges")]
    [Authorize(Policy = "CanManageRisk")]
    public async Task<IResult> StartChallenge([FromBody] StartThreeDsChallengeRequest req, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        return await _mediator.Send(new StartThreeDsChallengeCommand(req, actor, HttpContext.TraceIdentifier), ct);
    }

    [HttpPost("challenges/{id:guid}/verify")]
    [Authorize(Policy = "CanManageRisk")]
    public async Task<IResult> VerifyChallenge(Guid id, [FromBody] VerifyThreeDsChallengeRequest req, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        return await _mediator.Send(new VerifyThreeDsChallengeCommand(id, req, actor, HttpContext.TraceIdentifier), ct);
    }

    [HttpGet("challenges/{id:guid}")]
    [Authorize(Policy = "CanViewAudit")]
    public async Task<IResult> GetChallenge(Guid id, CancellationToken ct)
    {
        return await _mediator.Send(new GetThreeDsChallengeQuery(id), ct);
    }

    [HttpGet("challenges")]
    [Authorize(Policy = "CanViewAudit")]
    public async Task<IResult> ListChallenges([FromQuery] string? status, [FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new ListThreeDsChallengesQuery(status, take), ct);
    }
}
