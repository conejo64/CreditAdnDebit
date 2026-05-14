using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Settlement.Queries;

public record GetSettlementBatchesQuery(int Take) : IRequest<IResult>;
public class GetSettlementBatchesQueryHandler : IRequestHandler<GetSettlementBatchesQuery, IResult>
{
    private readonly SettlementService _settle;

    public GetSettlementBatchesQueryHandler(SettlementService settle)
    {
        _settle = settle;
    }

    public async Task<IResult> Handle(GetSettlementBatchesQuery request, CancellationToken cancellationToken)
    {
        var list = await _settle.GetBatchesAsync(request.Take <= 0 ? 50 : request.Take, cancellationToken);
        return Results.Ok(list);
    }
}

public record GetSettlementBatchQuery(Guid Id) : IRequest<IResult>;
public class GetSettlementBatchQueryHandler : IRequestHandler<GetSettlementBatchQuery, IResult>
{
    private readonly SettlementService _settle;

    public GetSettlementBatchQueryHandler(SettlementService settle)
    {
        _settle = settle;
    }

    public async Task<IResult> Handle(GetSettlementBatchQuery request, CancellationToken cancellationToken)
    {
        var b = await _settle.GetBatchAsync(request.Id, cancellationToken);
        return b is null ? Results.NotFound() : Results.Ok(b);
    }
}

public record GetSwitchJournalQuery(int Take) : IRequest<IResult>;
public class GetSwitchJournalQueryHandler : IRequestHandler<GetSwitchJournalQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public GetSwitchJournalQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(GetSwitchJournalQuery request, CancellationToken cancellationToken)
    {
        var list = await _db.TxnJournal.AsNoTracking().OrderByDescending(x => x.CreatedOn).Take(request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500)).ToListAsync(cancellationToken);
        return Results.Ok(list);
    }
}

public record ReconcileSettlementQuery(Guid BatchId) : IRequest<IResult>;
public class ReconcileSettlementQueryHandler : IRequestHandler<ReconcileSettlementQuery, IResult>
{
    private readonly CardVaultDbContext _db;

    public ReconcileSettlementQueryHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(ReconcileSettlementQuery request, CancellationToken cancellationToken)
    {
        var b = await _db.SettlementBatches.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == request.BatchId, cancellationToken);
        if (b is null)
            return Results.NotFound();
        var rrns = b.Items.Select(i => i.NetworkRef).ToList();
        var j = await _db.TxnJournal.AsNoTracking().Where(x => x.Network == b.Network.ToString() && rrns.Contains(x.Rrn)).ToListAsync(cancellationToken);
        var matched = j.Count;
        var missing = rrns.Count - matched;
        return Results.Ok(new { batchId = b.Id, network = b.Network.ToString(), businessDate = b.BusinessDate.ToString(), items = b.Items.Count, matchedJournals = matched, missingJournals = missing });
    }
}
