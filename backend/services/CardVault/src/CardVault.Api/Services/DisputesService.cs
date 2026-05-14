using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Switch;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class DisputesService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public DisputesService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity>> ListAsync(Guid accountId, int take, CancellationToken ct) =>
        _db.DisputeCases.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.OpenedOn)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    public Task<List<DisputeEventEntity>> ListEventsAsync(Guid disputeId, int take, CancellationToken ct) =>
        _db.DisputeEvents.AsNoTracking()
            .Where(x => x.DisputeId == disputeId)
            .OrderByDescending(x => x.CreatedOn)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    public Task<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity?> GetAsync(Guid id, CancellationToken ct) =>
        _db.DisputeCases.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity> TransitionAsync(Guid id, string action, string? notes, CancellationToken ct)
    {
        var d = await _db.DisputeCases.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Dispute not found");

        action = action.ToLowerInvariant();
        d.Status = action switch
        {
            "representment" => CardVault.Infrastructure.Persistence.Billing.DisputeStatus.Representment,
            "prearb" or "prearbitration" => CardVault.Infrastructure.Persistence.Billing.DisputeStatus.PreArbitration,
            "arbitration" => CardVault.Infrastructure.Persistence.Billing.DisputeStatus.Arbitration,
            _ => d.Status
        };

        _db.DisputeEvents.Add(new DisputeEventEntity
        {
            Id = Guid.NewGuid(),
            DisputeId = d.Id,
            Action = action,
            Notes = notes ?? ""
        });

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("dispute.v1.transitioned",
            new { disputeId = d.Id, action, notes },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return d;
    }

    public async Task<CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity> CloseAsync(Guid id, bool won, CancellationToken ct)
    {
        var d = await _db.DisputeCases.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Dispute not found");

        d.Status = won ? CardVault.Infrastructure.Persistence.Billing.DisputeStatus.Won : CardVault.Infrastructure.Persistence.Billing.DisputeStatus.Lost;
        d.ResolvedOn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("dispute.v1.closed",
            new { disputeId = d.Id, d.AccountId, d.Network, d.Rrn, d.OriginalAmount, won },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return d;
    }
}
