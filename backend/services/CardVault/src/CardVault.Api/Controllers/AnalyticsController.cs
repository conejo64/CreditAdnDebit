using CardVault.Application.Features.Analytics.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Policy = "CanViewAnalytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("dashboard")]
    public async Task<IResult> GetDashboard([FromQuery] int days, CancellationToken ct)
        => await _mediator.Send(new GetAnalyticsDashboardQuery(days), ct);

    [HttpGet("consumption")]
    public async Task<IResult> GetConsumption([FromQuery] int days, CancellationToken ct)
        => await _mediator.Send(new GetConsumptionAnalyticsQuery(days), ct);

    [HttpGet("fraud")]
    public async Task<IResult> GetFraud([FromQuery] int days, CancellationToken ct)
        => await _mediator.Send(new GetFraudAnalyticsQuery(days), ct);
}
