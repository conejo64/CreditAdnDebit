using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

/// <summary>
/// v39 - Daily interest accrual engine (simplified).
/// - Computes end-of-day balance (excluding Interest entries)
/// - If balance > 0: posts Interest ledger entry and stores InterestAccrualRecord
/// - Grace (demo): if previous balance at 'from' date is <= 0, skip first PurchaseGraceDays days.
/// </summary>
public sealed class DailyInterestAccrualService
{
    private readonly CardVaultDbContext _db;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;

    public DailyInterestAccrualService(CardVaultDbContext db, CreditPolicyService policies, AuditService audit)
    {
        _db = db;
        _policies = policies;
        _audit = audit;
    }

    public async Task<int> AccrueAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) throw new InvalidOperationException("Invalid date range");

        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found");

        if (acc.AccountType != AccountType.Credit) return 0;

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);

        // prev balance before start date (exclude interest)
        var prevBalance = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.PostedOn < from.ToDateTime(TimeOnly.MinValue) && x.Type != LedgerEntryType.Interest)
            .SumAsync(x => x.Amount, ct);

        var graceDays = (prevBalance <= 0) ? Math.Max(0, policy.PurchaseGraceDays) : 0;

        var created = 0;
        var dayIndex = 0;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            dayIndex++;

            // idempotency: if accrual already exists for day/segment skip
            var exists = await _db.InterestAccrualRecords.AsNoTracking()
                .AnyAsync(x => x.AccountId == accountId && x.AccrualDate == d && x.Segment == InterestSegment.Purchase, ct);
            if (exists) continue;

            // End-of-day balance: sum non-interest ledger up to end of day
            var eod = await _db.LedgerEntries.AsNoTracking()
                .Where(x => x.AccountId == accountId &&
                            x.PostedOn <= d.ToDateTime(TimeOnly.MaxValue) &&
                            x.Type != LedgerEntryType.Interest)
                .SumAsync(x => x.Amount, ct);

            var balanceBase = Math.Max(0, eod);

            // Apply demo grace window
            if (graceDays > 0 && dayIndex <= graceDays)
            {
                _db.InterestAccrualRecords.Add(new InterestAccrualRecordEntity
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    AccrualDate = d,
                    Segment = InterestSegment.Purchase,
                    BalanceBase = balanceBase,
                    Apr = policy.PurchaseApr,
                    DailyRate = policy.PurchaseApr / 365m,
                    InterestAmount = 0m,
                    LedgerEntryId = null,
                    CreatedOn = DateTimeOffset.UtcNow
                });

                created++;
                continue;
            }

            if (balanceBase <= 0) continue;

            var dailyRate = policy.PurchaseApr / 365m;
            var interest = Math.Round(balanceBase * dailyRate, 2, MidpointRounding.AwayFromZero);
            if (interest <= 0) continue;

            var ledgerId = Guid.NewGuid();

            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = ledgerId,
                AccountId = accountId,
                Type = LedgerEntryType.Interest,
                Amount = interest,
                Description = $"INTEREST - Daily (Purchase) {d:yyyy-MM-dd}",
                PostedOn = d.ToDateTime(TimeOnly.MaxValue),
                StatementId = null
            });

            _db.InterestAccrualRecords.Add(new InterestAccrualRecordEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                AccrualDate = d,
                Segment = InterestSegment.Purchase,
                BalanceBase = balanceBase,
                Apr = policy.PurchaseApr,
                DailyRate = dailyRate,
                InterestAmount = interest,
                LedgerEntryId = ledgerId,
                CreatedOn = DateTimeOffset.UtcNow
            });

            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(ct);

            await _audit.WriteAsync("interest.daily.accrued",
                new { accountId, from = from.ToString(), to = to.ToString(), created, policy.PurchaseApr, graceDays },
                correlationId: null,
                traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
                ct: ct);
        }

        return created;
    }
}
