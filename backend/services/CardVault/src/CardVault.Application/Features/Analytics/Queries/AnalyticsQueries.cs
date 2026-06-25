using CardVault.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Application.Features.Analytics.Queries;

public record GetAnalyticsDashboardQuery(int Days) : IRequest<IResult>;
public class GetAnalyticsDashboardQueryHandler : IRequestHandler<GetAnalyticsDashboardQuery, IResult>
{
    private readonly AnalyticsService _service;

    public GetAnalyticsDashboardQueryHandler(AnalyticsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetAnalyticsDashboardQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetDashboardAsync(request.Days, cancellationToken));
}

public record GetConsumptionAnalyticsQuery(int Days) : IRequest<IResult>;
public class GetConsumptionAnalyticsQueryHandler : IRequestHandler<GetConsumptionAnalyticsQuery, IResult>
{
    private readonly AnalyticsService _service;

    public GetConsumptionAnalyticsQueryHandler(AnalyticsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetConsumptionAnalyticsQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetConsumptionAsync(request.Days, cancellationToken));
}

public record GetFraudAnalyticsQuery(int Days) : IRequest<IResult>;
public class GetFraudAnalyticsQueryHandler : IRequestHandler<GetFraudAnalyticsQuery, IResult>
{
    private readonly AnalyticsService _service;

    public GetFraudAnalyticsQueryHandler(AnalyticsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetFraudAnalyticsQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetFraudAsync(request.Days, cancellationToken));
}
