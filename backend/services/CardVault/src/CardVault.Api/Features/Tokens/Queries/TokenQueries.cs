using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Api.Services;
using CardVault.Api.Vault;

namespace CardVault.Api.Features.Tokens.Queries;

public record GetTokenMetadataQuery(string Token) : IRequest<IResult>;
public class GetTokenMetadataQueryHandler : IRequestHandler<GetTokenMetadataQuery, IResult>
{
    private readonly TokenVaultService _svc;

    public GetTokenMetadataQueryHandler(TokenVaultService svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(GetTokenMetadataQuery request, CancellationToken cancellationToken)
    {
        var res = await _svc.GetMetadataAsync(request.Token, cancellationToken);
        return Results.Ok(res);
    }
}

public record GetActiveKeyQuery() : IRequest<IResult>;
public class GetActiveKeyQueryHandler : IRequestHandler<GetActiveKeyQuery, IResult>
{
    private readonly VaultSettingsStore _store;
    private readonly VaultOptions _options;

    public GetActiveKeyQueryHandler(VaultSettingsStore store, VaultOptions options)
    {
        _store = store;
        _options = options;
    }

    public async Task<IResult> Handle(GetActiveKeyQuery request, CancellationToken cancellationToken)
    {
        var active = await _store.GetActiveKeyIdAsync(cancellationToken);
        var available = _options.Keys.Keys.OrderBy(k => k).ToList();
        return Results.Ok(new { activeKeyId = active, availableKeyIds = available });
    }
}

public record GetAuditLatestQuery(int Take) : IRequest<IResult>;
public class GetAuditLatestQueryHandler : IRequestHandler<GetAuditLatestQuery, IResult>
{
    private readonly AuditService _audit;

    public GetAuditLatestQueryHandler(AuditService audit)
    {
        _audit = audit;
    }

    public async Task<IResult> Handle(GetAuditLatestQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 500);
        var list = await _audit.LatestAsync(take, cancellationToken);
        return Results.Ok(list);
    }
}

public record GetOutboxLatestQuery(int Take) : IRequest<IResult>;
public class GetOutboxLatestQueryHandler : IRequestHandler<GetOutboxLatestQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetOutboxLatestQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetOutboxLatestQuery request, CancellationToken cancellationToken)
    {
        int take = request.Take <= 0 ? 50 : Math.Min(request.Take, 200);
        var list = await _db.OutboxMessages.AsNoTracking().OrderByDescending(x => x.OccurredOn).Take(take).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}
