using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;

namespace CardVault.Api.Features.Risk.Queries;

public record GetInterestAccrualsQuery(Guid AccountId, int Take) : IRequest<IResult>;
public class GetInterestAccrualsQueryHandler : IRequestHandler<GetInterestAccrualsQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetInterestAccrualsQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetInterestAccrualsQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 500);
        var list = await _db.InterestAccrualRecords.AsNoTracking().Where(x => x.AccountId == request.AccountId).OrderByDescending(x => x.AccrualDate).Take(take).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}

public record GetMccRulesQuery() : IRequest<IResult>;
public class GetMccRulesQueryHandler : IRequestHandler<GetMccRulesQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetMccRulesQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetMccRulesQuery request, CancellationToken cancellationToken)
    {
        var list = await _db.MccRules.AsNoTracking().OrderBy(x => x.Mcc).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}

public record GetVelocityRulesQuery(string ProductCode) : IRequest<IResult>;
public class GetVelocityRulesQueryHandler : IRequestHandler<GetVelocityRulesQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetVelocityRulesQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetVelocityRulesQuery request, CancellationToken cancellationToken)
    {
        var list = await _db.VelocityRules.AsNoTracking().Where(x => x.ProductCode == request.ProductCode).OrderBy(x => x.WindowMinutes).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}
