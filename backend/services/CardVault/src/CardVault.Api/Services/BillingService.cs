using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class BillingService
{
    private readonly CardVaultDbContext _db;
    private readonly MinimumPaymentService _minPay;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;

    public BillingService(CardVaultDbContext db, MinimumPaymentService minPay, CreditPolicyService policies, AuditService audit)
    {
        _db = db;
        _minPay = minPay;
        _policies = policies;
        _audit = audit;
    }

    public async Task<StatementEntity> GenerateStatementAsync(Guid accountId, DateTime cycleStart, DateTime cycleEnd, DateTime statementDate, DateTime? dueDateOverride, CancellationToken ct)
    {
        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        if (acc is null) throw new InvalidOperationException("Account not found");
        if (acc.AccountType != AccountType.Credit) throw new InvalidOperationException("Statements are only supported for credit accounts");

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);
        var dueDate = dueDateOverride ?? statementDate.AddDays(policy.GraceDays);

        var exists = await _db.Statements.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.StatementDate == statementDate, ct);
        if (exists) throw new InvalidOperationException("Statement already generated for this date");

        var cycleStartDt = cycleStart;
        var cycleEndDt = cycleEnd;

        // Previous balance: sum all ledger before cycle
        var prevBalance = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.PostedOn < cycleStartDt)
            .SumAsync(x => x.Amount, ct);

        // Cycle entries not yet assigned to a statement
        var cycleEntries = await _db.LedgerEntries
            .Where(x => x.AccountId == accountId &&
                        x.PostedOn >= cycleStartDt &&
                        x.PostedOn <= cycleEndDt &&
                        x.StatementId == null)
            .OrderBy(x => x.PostedOn)
            .ToListAsync(ct);

        var purchases = cycleEntries.Where(x => x.Type == LedgerEntryType.Purchase || x.Type == LedgerEntryType.Clearing || x.Type == LedgerEntryType.Refund || x.Type == LedgerEntryType.Reversal || x.Type == LedgerEntryType.Chargeback || x.Type == LedgerEntryType.Adjustment).Sum(x => x.Amount);
        var payments = cycleEntries.Where(x => x.Type == LedgerEntryType.Payment).Sum(x => x.Amount); // negative
        var fees = cycleEntries.Where(x => x.Type == LedgerEntryType.Fee).Sum(x => x.Amount);
        var interest = cycleEntries.Where(x => x.Type == LedgerEntryType.Interest).Sum(x => x.Amount);

        // v66 - Installments due in this cycle
        var dueInstallments = await _db.AmortizationSchedules
            .Where(x => x.Status == InstallmentStatus.Pending && x.DueDate <= cycleEndDt)
            .Join(_db.InstallmentPlans.Where(p => p.AccountId == accountId),
                s => s.PlanId,
                p => p.Id,
                (s, p) => s)
            .ToListAsync(ct);

        var installmentDue = dueInstallments.Sum(x => x.TotalInstallmentAmount);

        // Average daily balance excluding interest ledger entries (for display)
        static decimal ComputeAverageDailyBalance(decimal prevBalance, List<LedgerEntryEntity> entries, DateTime cycleStart, DateTime cycleEnd)
        {
            var start = cycleStart.Date;
            var end = cycleEnd.Date;
            var days = (end - start).Days + 1;
            if (days <= 0) return 0m;

            var byDate = entries
                .GroupBy(e => e.PostedOn.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            decimal running = prevBalance;
            decimal sum = 0m;

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (byDate.TryGetValue(d, out var delta))
                    running += delta;

                sum += running;
            }

            return Math.Round(sum / days, 2, MidpointRounding.AwayFromZero);
        }

        var nonInterest = cycleEntries.Where(x => x.Type != LedgerEntryType.Interest).ToList();
        var adb = ComputeAverageDailyBalance(prevBalance, nonInterest, cycleStart, cycleEnd);
        var interestDays = (cycleEnd.Date - cycleStart.Date).Days + 1;

        var newBalance = prevBalance + purchases + payments + fees + interest + installmentDue;

        var st = new StatementEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CycleStart = cycleStart,
            CycleEnd = cycleEnd,
            StatementDate = statementDate,
            DueDate = dueDate,
            PreviousBalance = prevBalance,
            Purchases = purchases,
            Payments = payments,
            Fees = fees,
            Interest = interest,
            AverageDailyBalance = adb,
            InterestApr = policy.PurchaseApr, // displayed APR for purchases segment
            InterestDays = interestDays,
            NewBalance = newBalance,
            Status = StatementStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        };

        // v37/v38 buckets + minimum
        st.InterestAccrued = interest;
        st.LateFeeAmount = 0m;
        ApplyClosingTotals(st);

        var mpPolicy = await _minPay.GetDefaultAsync(ct);
        st.MinimumPayment = _minPay.CalculateMinimum(st, mpPolicy);

        _db.Statements.Add(st);

        foreach (var e in cycleEntries)
        {
            e.StatementId = st.Id;
            _db.StatementLines.Add(new StatementLineEntity
            {
                Id = Guid.NewGuid(),
                StatementId = st.Id,
                LedgerEntryId = e.Id,
                PostedOn = e.PostedOn,
                Type = e.Type,
                Amount = e.Amount,
                Description = e.Description
            });
        }

        // v66 - Add installments as lines and update status
        foreach (var inst in dueInstallments)
        {
            inst.Status = InstallmentStatus.Invoiced;
            inst.BilledStatementId = st.Id;
            inst.BilledOn = DateTimeOffset.UtcNow;

            _db.StatementLines.Add(new StatementLineEntity
            {
                Id = Guid.NewGuid(),
                StatementId = st.Id,
                LedgerEntryId = null,
                PostedOn = inst.DueDate,
                Type = LedgerEntryType.Fee,
                Amount = inst.TotalInstallmentAmount,
                Description = $"CUOTA {inst.InstallmentNumber} - Plan Diferido"
            });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.statement.generated",
            new { accountId, cycleStart, cycleEnd, statementDate, dueDate, newBalance, interest, adb },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return st;
    }

    public async Task<StatementEntity> ApplyStatementPaymentAsync(Guid statementId, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        if (amount <= 0) throw new InvalidOperationException("Payment amount must be > 0");

        var st = await _db.Statements.FirstOrDefaultAsync(x => x.Id == statementId, ct);
        if (st is null) throw new InvalidOperationException("Statement not found");

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = st.AccountId,
            Type = LedgerEntryType.Payment,
            Amount = -Math.Abs(amount),
            Description = "PAYMENT - Statement payment",
            PostedOn = postedOn,
            StatementId = st.Id
        });

        st.PaidAmount += amount;

        // totals from buckets
        if (st.PrincipalDue + st.InterestDue + st.FeesDue > 0)
        {
            st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
            st.NewBalance = st.TotalPaymentDue;
        }

        var policy = await _minPay.GetDefaultAsync(ct);
        st.MinimumPayment = _minPay.CalculateMinimum(st, policy);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.statement.payment_applied",
            new { statementId, st.AccountId, amount, st.PaidAmount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return st;
    }

    public async Task<StatementEntity?> ApplyLateFeeIfNeededAsync(Guid statementId, bool force, CancellationToken ct)
    {
        var st = await _db.Statements.FirstOrDefaultAsync(x => x.Id == statementId, ct);
        if (st is null) return null;

        if (st.Status != StatementStatus.Open) return st;
        if (st.LateFeeAppliedOn is not null && !force) return st;

        var now = DateTimeOffset.UtcNow;
        var isPastDue = now.Date > st.DueDate.Date;
        if (!force && !isPastDue) return st;

        if (st.PaidAmount >= st.MinimumPayment && !force) return st;

        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == st.AccountId, ct);
        if (acc is null) return st;

        var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);
        var fee = policy.LateFee;
        if (fee <= 0) return st;

        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = st.AccountId,
            Type = LedgerEntryType.Fee,
            Amount = Math.Abs(fee),
            Description = "FEE - Late payment",
            PostedOn = now,
            StatementId = st.Id
        });

        st.LateFeeAppliedOn = now;
        st.LateFeeAmount += fee;

        if (st.PrincipalDue + st.InterestDue + st.FeesDue > 0)
        {
            st.FeesDue += fee;
        }

        st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
        st.NewBalance = st.TotalPaymentDue;

        var mpPolicy = await _minPay.GetDefaultAsync(ct);
        st.MinimumPayment = _minPay.CalculateMinimum(st, mpPolicy);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.statement.late_fee_applied",
            new { statementId, st.AccountId, fee, force },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return st;
    }
    public async Task<StatementEntity?> GetStatementAsync(Guid statementId, CancellationToken ct)
    {
        return await _db.Statements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == statementId, ct);
    }

    public async Task<List<StatementLineEntity>> GetLinesAsync(Guid statementId, CancellationToken ct)
    {
        return await _db.StatementLines.AsNoTracking().Where(x => x.StatementId == statementId).OrderBy(x => x.PostedOn).ToListAsync(ct);
    }

    public async Task<List<StatementEntity>> GetStatementsForAccountAsync(Guid accountId, int take, CancellationToken ct)
    {
        return await _db.Statements.AsNoTracking().Where(x => x.AccountId == accountId).OrderByDescending(x => x.StatementDate).Take(take).ToListAsync(ct);
    }

    /// <summary>
    /// Applies the closing-totals formula to an already-populated statement entity.
    /// Caller is responsible for setting InterestAccrued and Fees (+ NewBalance for the
    /// consumer path) before calling this method.
    ///
    /// ADR-6: single source of truth for the terminal bucket-to-totals formula used by
    /// both GenerateStatementAsync and SwitchTxnConsumer.UpdateOpenStatementAsync.
    /// </summary>
    internal void ApplyClosingTotals(StatementEntity st)
    {
        st.InterestDue = st.InterestAccrued;
        // v40 - FeesDue includes all fees in cycle (overlimit/annual/cash-advance/late fees)
        st.FeesDue = st.Fees;
        st.PrincipalDue = Math.Max(0, st.NewBalance - st.InterestDue - st.FeesDue);
        st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
        st.NewBalance = st.TotalPaymentDue;
    }
}