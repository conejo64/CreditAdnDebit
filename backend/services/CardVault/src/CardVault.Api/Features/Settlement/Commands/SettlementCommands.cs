using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Settlement.Commands;

public record RunSettlementCommand(string Network, DateOnly BusinessDate) : IRequest<IResult>;
public class RunSettlementCommandHandler : IRequestHandler<RunSettlementCommand, IResult>
{
    private readonly SettlementService _settle;

    public RunSettlementCommandHandler(SettlementService settle)
    {
        _settle = settle;
    }

    public async Task<IResult> Handle(RunSettlementCommand request, CancellationToken cancellationToken)
    {
        var batch = await _settle.RunDailySettlementAsync(request.Network, request.BusinessDate, cancellationToken);
        return Results.Ok(batch);
    }
}

public record ExpireHoldsCommand() : IRequest<IResult>;
public class ExpireHoldsCommandHandler : IRequestHandler<ExpireHoldsCommand, IResult>
{
    private readonly HoldMaintenanceService _svc;

    public ExpireHoldsCommandHandler(HoldMaintenanceService svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(ExpireHoldsCommand request, CancellationToken cancellationToken)
    {
        var expired = await _svc.ExpireDueHoldsAsync(DateTimeOffset.UtcNow, cancellationToken);
        return Results.Ok(new { expired });
    }
}
