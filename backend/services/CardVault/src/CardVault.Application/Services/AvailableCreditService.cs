using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class AvailableCreditService
{
    private readonly CardVaultDbContext _db;

    public AvailableCreditService(CardVaultDbContext db)
    {
        _db = db;
    }

    public sealed record AvailableCreditResult(Guid AccountId, decimal CreditLimit, decimal PostedBalance, decimal ActiveHolds, decimal AvailableCredit);

    public async Task<AvailableCreditResult> GetAsync(Guid accountId, CancellationToken ct)
    {
        var acct = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found");

        // Posted balance excludes AuthorizationHold entries (shadow). Clearing/Purchase/etc included.
        var postedBalance = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Type != LedgerEntryType.AuthorizationHold)
            .SumAsync(x => x.Amount, ct);

                var activeHolds = await _db.AuthorizationHolds.AsNoTracking()
            .Where(x => x.AccountId == accountId && (x.Status == HoldStatus.Active || x.Status == HoldStatus.PartiallyCaptured))
            .SumAsync(x => (x.Amount - x.CapturedAmount), ct);

        var available = acct.CreditLimit - postedBalance - activeHolds;
        if (available < 0) available = 0;

        return new AvailableCreditResult(accountId, acct.CreditLimit, postedBalance, activeHolds, available);
    }
}

