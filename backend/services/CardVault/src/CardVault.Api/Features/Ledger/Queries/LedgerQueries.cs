using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Ledger.Queries;

public record GetBalanceQuery(Guid AccountId) : IRequest<IResult>;
public class GetBalanceQueryHandler : IRequestHandler<GetBalanceQuery, IResult>
{
    private readonly LedgerService _ledger;

    public GetBalanceQueryHandler(LedgerService ledger)
    {
        _ledger = ledger;
    }

    public async Task<IResult> Handle(GetBalanceQuery request, CancellationToken cancellationToken)
    {
        var bal = await _ledger.GetBalanceAsync(request.AccountId, cancellationToken);
        return Results.Ok(new { request.AccountId, balance = bal });
    }
}

public record GetMovementsQuery(Guid AccountId, int Take) : IRequest<IResult>;
public class GetMovementsQueryHandler : IRequestHandler<GetMovementsQuery, IResult>
{
    private readonly LedgerService _ledger;

    public GetMovementsQueryHandler(LedgerService ledger)
    {
        _ledger = ledger;
    }

    public async Task<IResult> Handle(GetMovementsQuery request, CancellationToken cancellationToken)
    {
        var entries = await _ledger.GetMovementsAsync(request.AccountId, request.Take, cancellationToken);
        return Results.Ok(entries);
    }
}
