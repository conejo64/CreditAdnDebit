using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class DisputeService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public DisputeService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<DisputeCaseEntity> OpenChargebackAsync(Guid accountId, string network, string stan, string rrn, string reasonCode, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        var existing = await _db.DisputeCases.FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network && x.Stan == stan && x.Rrn == rrn, ct);

        if (existing is not null) return existing;

        var j = await _db.TxnJournal.AsNoTracking().FirstOrDefaultAsync(x =>
            x.AccountId == accountId && x.Network == network && x.Stan == stan && x.Rrn == rrn, ct);

        var provisionalLedgerId = Guid.NewGuid();

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = provisionalLedgerId,
            AccountId = accountId,
            Type = LedgerEntryType.Chargeback,
            Amount = -Math.Abs(amount),
            Description = $"CHARGEBACK - Provisional credit ({reasonCode})",
            PostedOn = postedOn,
            StatementId = null
        });

        var c = new DisputeCaseEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            OriginalTxnJournalId = j?.Id,
            Network = network,
            Stan = stan,
            Rrn = rrn,
            ReasonCode = reasonCode,
            OriginalAmount = Math.Abs(amount),
            Status = DisputeStatus.Open,
            ProvisionalCreditLedgerEntryId = provisionalLedgerId
        };

        _db.DisputeCases.Add(c);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("disputes.chargeback.opened",
            new { accountId, network, stan, rrn, reasonCode, amount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return c;
    }

    public async Task<DisputeCaseEntity> ResolveAsync(Guid disputeId, DisputeStatus outcome, DateTimeOffset? resolvedOn, string? notes, CancellationToken ct)
    {
        var c = await _db.DisputeCases.FirstOrDefaultAsync(x => x.Id == disputeId, ct)
            ?? throw new InvalidOperationException("Dispute not found");

        if (c.Status != DisputeStatus.Open) return c;

        c.Status = outcome;
        c.ResolvedOn = resolvedOn ?? DateTimeOffset.UtcNow;
        c.Notes = notes;

        if (outcome == DisputeStatus.Lost && c.OriginalAmount > 0)
        {
            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = c.AccountId,
                Type = LedgerEntryType.Adjustment,
                Amount = Math.Abs(c.OriginalAmount),
                Description = "CHARGEBACK - Lost (re-debit)",
                PostedOn = c.ResolvedOn.Value,
                StatementId = null
            });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("disputes.chargeback.resolved",
            new { disputeId, c.AccountId, outcome = outcome.ToString(), notes },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return c;
    }
}
