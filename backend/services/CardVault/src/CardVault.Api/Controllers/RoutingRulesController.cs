using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Application.Features.RoutingRules.Commands;
using CardVault.Application.Features.RoutingRules.Queries;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Routing;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/routing-rules")]
[Authorize(Policy = "CanManageSwitchRoutes")]
public class RoutingRulesController : ControllerBase
{
    private readonly IMediator _mediator;

    public RoutingRulesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetRules(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetRoutingRulesQuery(), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IResult> CreateRule([FromBody] RoutingRuleEntity rule, CancellationToken ct)
    {
        return await _mediator.Send(new CreateRoutingRuleCommand(rule), ct);
    }
}
