using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class LedgerService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly AccountingService _accounting;

    public LedgerService(CardVaultDbContext db, AuditService audit, AccountingService accounting)
    {
        _db = db;
        _audit = audit;
        _accounting = accounting;
    }

    public async Task<LedgerEntryEntity> AddEntryAsync(Guid accountId, LedgerEntryType type, decimal amount, string description, DateTimeOffset postedOn, CancellationToken ct)
    {
        if (type == LedgerEntryType.Payment && amount > 0) amount = -amount; // payments are negative
        if (type != LedgerEntryType.Payment && amount < 0) amount = Math.Abs(amount);

        var e = new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = description,
            PostedOn = postedOn,
            StatementId = null
        };

        _db.LedgerEntries.Add(e);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("ledger.entry.posted",
            new { entryId = e.Id, accountId, type = type.ToString(), amount = e.Amount, description },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        await _accounting.GenerateForLedgerEntryAsync(e, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "ledger", ct);

        return e;
    }

    public Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken ct) =>
        _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .SumAsync(x => x.Amount, ct);

    public Task<List<LedgerEntryEntity>> GetMovementsAsync(Guid accountId, int take, CancellationToken ct)
    {
        var q = _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.PostedOn);
        return (take > 0 ? q.Take(take) : q).ToListAsync(ct);
    }
}
