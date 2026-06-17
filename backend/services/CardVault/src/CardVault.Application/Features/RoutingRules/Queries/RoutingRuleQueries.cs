using MediatR;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Routing;

namespace CardVault.Application.Features.RoutingRules.Queries;

public record GetRoutingRulesQuery() : IRequest<List<RoutingRuleEntity>>;

public class GetRoutingRulesQueryHandler : IRequestHandler<GetRoutingRulesQuery, List<RoutingRuleEntity>>
{
    private readonly CardVaultDbContext _db;

    public GetRoutingRulesQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<List<RoutingRuleEntity>> Handle(GetRoutingRulesQuery request, CancellationToken cancellationToken)
    {
        return await _db.RoutingRules.OrderBy(x => x.Priority).ToListAsync(cancellationToken);
    }
}
