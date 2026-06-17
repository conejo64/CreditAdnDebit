using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class FeeService
{
    private readonly CardVaultDbContext _db;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;

    public FeeService(CardVaultDbContext db, CreditPolicyService policies, AuditService audit)
    {
        _db = db;
        _policies = policies;
        _audit = audit;
    }

    public async Task<FeeAssessmentEntity?> AssessOverlimitAsync(Guid accountId, DateOnly businessDate, DateTimeOffset postedOn, CancellationToken ct)
    {
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (acc is null) return null;
        if (acc.AccountType != AccountType.Credit) return null;

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);
        if (policy.OverlimitFee <= 0) return null;

        // Determine current balance (exclude Interest) for overlimit check
        var bal = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Type != LedgerEntryType.Interest)
            .SumAsync(x => x.Amount, ct);

        if (bal <= acc.CreditLimit) return null;

        // Idempotency
        var exists = await _db.FeeAssessments.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.FeeType == FeeType.Overlimit && x.BusinessDate == businessDate, ct);

        if (exists && policy.OverlimitFeeOncePerDay) return null;

        var ledgerId = Guid.NewGuid();

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = ledgerId,
            AccountId = accountId,
            Type = LedgerEntryType.Fee,
            Amount = Math.Abs(policy.OverlimitFee),
            Description = "FEE - Overlimit",
            PostedOn = postedOn,
            StatementId = null
        });

        var fee = new FeeAssessmentEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FeeType = FeeType.Overlimit,
            BusinessDate = businessDate,
            Amount = policy.OverlimitFee,
            LedgerEntryId = ledgerId,
            Notes = $"Balance {bal} > limit {acc.CreditLimit}"
        };

        _db.FeeAssessments.Add(fee);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("fees.overlimit.assessed",
            new { accountId, businessDate = businessDate.ToString(), fee = policy.OverlimitFee, bal, acc.CreditLimit },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return fee;
    }

    public async Task<FeeAssessmentEntity?> AssessAnnualAsync(Guid accountId, DateOnly businessDate, DateTimeOffset postedOn, CancellationToken ct)
    {
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (acc is null) return null;
        if (acc.AccountType != AccountType.Credit) return null;

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);
        if (policy.AnnualFee <= 0) return null;

        var exists = await _db.FeeAssessments.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.FeeType == FeeType.Annual && x.BusinessDate == businessDate, ct);
        if (exists) return null;

        var ledgerId = Guid.NewGuid();

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = ledgerId,
            AccountId = accountId,
            Type = LedgerEntryType.Fee,
            Amount = Math.Abs(policy.AnnualFee),
            Description = "FEE - Annual",
            PostedOn = postedOn,
            StatementId = null
        });

        var fee = new FeeAssessmentEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FeeType = FeeType.Annual,
            BusinessDate = businessDate,
            Amount = policy.AnnualFee,
            LedgerEntryId = ledgerId,
            Notes = "Annual fee"
        };

        _db.FeeAssessments.Add(fee);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("fees.annual.assessed",
            new { accountId, businessDate = businessDate.ToString(), fee = policy.AnnualFee },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return fee;
    }

    public async Task<FeeAssessmentEntity?> AssessCashAdvanceAsync(Guid accountId, DateOnly businessDate, DateTimeOffset postedOn, decimal cashAmount, CancellationToken ct)
    {
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (acc is null) return null;
        if (acc.AccountType != AccountType.Credit) return null;

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);

        var feeAmount = Math.Round(Math.Abs(policy.CashAdvanceFeeFixed) + (Math.Abs(policy.CashAdvanceFeePercent) * Math.Abs(cashAmount)), 2, MidpointRounding.AwayFromZero);
        if (feeAmount <= 0) return null;

        // idempotency by businessDate (demo)
        var exists = await _db.FeeAssessments.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.FeeType == FeeType.CashAdvance && x.BusinessDate == businessDate, ct);
        if (exists) return null;

        var ledgerId = Guid.NewGuid();

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = ledgerId,
            AccountId = accountId,
            Type = LedgerEntryType.Fee,
            Amount = Math.Abs(feeAmount),
            Description = $"FEE - Cash advance ({cashAmount:0.00})",
            PostedOn = postedOn,
            StatementId = null
        });

        var fee = new FeeAssessmentEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FeeType = FeeType.CashAdvance,
            BusinessDate = businessDate,
            Amount = feeAmount,
            LedgerEntryId = ledgerId,
            Notes = $"cashAmount={cashAmount:0.00}"
        };

        _db.FeeAssessments.Add(fee);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("fees.cash_advance.assessed",
            new { accountId, businessDate = businessDate.ToString(), fee = feeAmount, cashAmount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return fee;
    }
}
