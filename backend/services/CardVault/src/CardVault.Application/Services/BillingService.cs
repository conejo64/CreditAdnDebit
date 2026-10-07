using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardVault.Application.Services;

public sealed class BillingService
{
    private readonly CardVaultDbContext _db;
    private readonly MinimumPaymentService _minPay;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;
    private readonly ILogger<BillingService> _logger;

    public BillingService(CardVaultDbContext db, MinimumPaymentService minPay, CreditPolicyService policies, AuditService audit, ILogger<BillingService>? logger = null)
    {
        _db = db;
        _minPay = minPay;
        _policies = policies;
        _audit = audit;
        _logger = logger ?? NullLogger<BillingService>.Instance;
    }

    public async Task<StatementEntity> GenerateStatementAsync(Guid accountId, DateTime cycleStart, DateTime cycleEnd, DateTime statementDate, DateTime? dueDateOverride, CancellationToken ct)
    {
        // Service boundary: every incoming DateTime is persisted to `timestamp with time zone`, which
        // Npgsql only accepts as Kind=Utc. A date-only request body ("2025-01-31") arrives as
        // Kind=Unspecified and is a calendar date, so it keeps its wall-clock value and is relabelled
        // UTC; a Local value is an instant and is converted. See UtcCalendarDate.
        cycleStart = UtcCalendarDate.Normalize(cycleStart);
        cycleEnd = UtcCalendarDate.Normalize(cycleEnd);
        statementDate = UtcCalendarDate.Normalize(statementDate);
        dueDateOverride = UtcCalendarDate.Normalize(dueDateOverride);

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

        // Active plans are resolved first so that a legacy plan can be healed before the ledger is
        // read: the heal changes the type of the original entry, which decides whether it belongs
        // to the purchase base of this cycle or to the parked deferred principal.
        var activePlans = await _db.InstallmentPlans
            .Where(p => p.AccountId == accountId && p.Status == InstallmentPlanStatus.Active)
            .ToListAsync(ct);
        var billablePlans = await HealLegacyPlansAsync(activePlans, ct);

        // Previous balance: billable ledger before the cycle (see LedgerEntryTypeExtensions.IsBillable).
        // Authorization holds are shadow entries: an open hold is not posted debt and a captured hold is
        // already counted through its Clearing. Deferred principal is owed but is billed through
        // installments, so it never enters a statement balance directly.
        var prevBalance = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId &&
                        x.PostedOn < cycleStartDt &&
                        x.Type != LedgerEntryType.AuthorizationHold &&
                        x.Type != LedgerEntryType.DeferredPrincipal)
            .SumAsync(x => x.Amount, ct);

        // Billable cycle entries not yet assigned to a statement. Shadow and parked entries are left
        // out entirely: they are never invoiced, so they get no statement line and keep StatementId null.
        var cycleEntries = await _db.LedgerEntries
            .Where(x => x.AccountId == accountId &&
                        x.PostedOn >= cycleStartDt &&
                        x.PostedOn <= cycleEndDt &&
                        x.StatementId == null &&
                        x.Type != LedgerEntryType.AuthorizationHold &&
                        x.Type != LedgerEntryType.DeferredPrincipal)
            .OrderBy(x => x.PostedOn)
            .ToListAsync(ct);

        // Purchases: debit purchases, clearings and signed adjustments. Credit types (refund, reversal,
        // chargeback) are negative by the ledger sign contract and are reported with payments.
        var purchases = cycleEntries.Where(x => x.Type == LedgerEntryType.Purchase || x.Type == LedgerEntryType.Clearing || x.Type == LedgerEntryType.Adjustment).Sum(x => x.Amount);
        var payments = cycleEntries.Where(x => x.Type == LedgerEntryType.Payment || x.Type == LedgerEntryType.Refund || x.Type == LedgerEntryType.Reversal || x.Type == LedgerEntryType.Chargeback).Sum(x => x.Amount); // negative
        var fees = cycleEntries.Where(x => x.Type == LedgerEntryType.Fee).Sum(x => x.Amount);
        var interest = cycleEntries.Where(x => x.Type == LedgerEntryType.Interest).Sum(x => x.Amount);

        // Installments due in this cycle. Each one is billed once: its principal moves from the
        // DeferredPrincipal bucket into an Installment ledger debit, and its plan interest is posted
        // as an Interest entry so it lands in the interest bucket and never compounds through daily accrual.
        // Only plans with a positive DeferredPrincipal counterpart are billed (see HealLegacyPlansAsync).
        var activePlanIds = billablePlans.Select(p => p.Id).ToList();

        var dueInstallments = await _db.AmortizationSchedules
            .Where(x => x.Status == InstallmentStatus.Pending && x.DueDate <= cycleEndDt && activePlanIds.Contains(x.PlanId))
            .OrderBy(x => x.DueDate).ThenBy(x => x.InstallmentNumber)
            .ToListAsync(ct);

        var installmentPrincipalDue = dueInstallments.Sum(x => x.PrincipalAmount);
        var installmentInterestDue = dueInstallments.Sum(x => x.InterestAmount);
        interest += installmentInterestDue;

        // Average daily balance excluding interest ledger entries (for display)
        // Delegate pure computation to Domain calculator — map entities to primitives at this boundary
        var nonInterest = cycleEntries.Where(x => x.Type != LedgerEntryType.Interest && x.Type.IsBillable()).ToList();
        var adbEntries = nonInterest
            .Select(e => (e.PostedOn.Date, e.Amount))
            .ToList();
        var adb = AverageDailyBalanceCalculator.Compute(prevBalance, adbEntries, cycleStart, cycleEnd);
        var interestDays = (cycleEnd.Date - cycleStart.Date).Days + 1;

        var newBalance = prevBalance + purchases + payments + fees + interest + installmentPrincipalDue;

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

        // Bill each due installment: ledger entries dated at the cycle close and attached to this
        // statement, plus the matching statement lines. The DeferredPrincipal release has no line of
        // its own; it only keeps the ledger exposure constant while principal becomes billable.
        var billedOn = new DateTimeOffset(DateTime.SpecifyKind(cycleEnd, DateTimeKind.Utc), TimeSpan.Zero);
        var plansById = billablePlans.ToDictionary(p => p.Id);

        foreach (var inst in dueInstallments)
        {
            inst.Status = InstallmentStatus.Invoiced;
            inst.BilledStatementId = st.Id;
            inst.BilledOn = DateTimeOffset.UtcNow;

            var plan = plansById[inst.PlanId];
            plan.RemainingInstallments = Math.Max(0, plan.RemainingInstallments - 1);
            if (plan.RemainingInstallments == 0) plan.Status = InstallmentPlanStatus.Completed;

            var label = $"CUOTA {inst.InstallmentNumber}/{plan.TotalInstallments} - {plan.Description}";

            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Type = LedgerEntryType.DeferredPrincipal,
                Amount = -inst.PrincipalAmount,
                Description = $"DEFERRED PRINCIPAL RELEASE - {label}",
                PostedOn = billedOn,
                StatementId = st.Id
            });

            var installmentEntry = new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Type = LedgerEntryType.Installment,
                Amount = inst.PrincipalAmount,
                Description = label,
                PostedOn = billedOn,
                StatementId = st.Id
            };
            _db.LedgerEntries.Add(installmentEntry);
            _db.StatementLines.Add(new StatementLineEntity
            {
                Id = Guid.NewGuid(),
                StatementId = st.Id,
                LedgerEntryId = installmentEntry.Id,
                PostedOn = billedOn,
                Type = LedgerEntryType.Installment,
                Amount = inst.PrincipalAmount,
                Description = label
            });

            if (inst.InterestAmount > 0m)
            {
                var interestEntry = new LedgerEntryEntity
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Type = LedgerEntryType.Interest,
                    Amount = inst.InterestAmount,
                    Description = $"INTEREST - {label}",
                    PostedOn = billedOn,
                    StatementId = st.Id
                };
                _db.LedgerEntries.Add(interestEntry);
                _db.StatementLines.Add(new StatementLineEntity
                {
                    Id = Guid.NewGuid(),
                    StatementId = st.Id,
                    LedgerEntryId = interestEntry.Id,
                    PostedOn = billedOn,
                    Type = LedgerEntryType.Interest,
                    Amount = inst.InterestAmount,
                    Description = $"INTEREST - {label}"
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.statement.generated",
            new { accountId, cycleStart, cycleEnd, statementDate, dueDate, newBalance, interest, adb },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return st;
    }

    /// <summary>
    /// Defensive guard for installment plans written before the deferred-principal model (Gate 0 /
    /// T2b). A plan may only release principal when its original ledger entry is a positive
    /// <see cref="LedgerEntryType.DeferredPrincipal"/>; otherwise the negative release posted at billing
    /// would have no counterpart and the original would stay in the purchase base (double billing).
    /// <para>
    /// A legacy original still typed <see cref="LedgerEntryType.Clearing"/> or <see cref="LedgerEntryType.Purchase"/>
    /// is reclassified in flight, exactly as <see cref="InstallmentService.DeferPurchaseAsync"/> would have done:
    /// same amount, positive, so the account exposure is unchanged. This self-heals one plan at a time and
    /// is idempotent; the one-off SQL backfill (<c>docs/runbooks/cardvault-ledger-legacy-backfill.md</c>)
    /// does the same for a whole database. A plan whose original entry is missing or has an unexpected type
    /// cannot be healed and is left out of billing for a human to reconcile; its schedule stays Pending.
    /// </para>
    /// </summary>
    /// <returns>The plans whose installments may be billed in this cycle.</returns>
    private async Task<List<InstallmentPlanEntity>> HealLegacyPlansAsync(List<InstallmentPlanEntity> activePlans, CancellationToken ct)
    {
        if (activePlans.Count == 0) return activePlans;

        var originalIds = activePlans.Where(p => p.OriginalLedgerEntryId is not null).Select(p => p.OriginalLedgerEntryId!.Value).ToList();
        var originals = await _db.LedgerEntries
            .Where(e => originalIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var billable = new List<InstallmentPlanEntity>(activePlans.Count);
        var healed = 0;

        foreach (var plan in activePlans)
        {
            if (plan.OriginalLedgerEntryId is not { } originalId || !originals.TryGetValue(originalId, out var original))
            {
                _logger.LogError(
                    "Installment plan {PlanId} on account {AccountId} has no original ledger entry (OriginalLedgerEntryId={OriginalLedgerEntryId}); its installments are not billed until reconciled",
                    plan.Id, plan.AccountId, plan.OriginalLedgerEntryId);
                continue;
            }

            switch (original.Type)
            {
                case LedgerEntryType.DeferredPrincipal:
                    billable.Add(plan);
                    break;

                case LedgerEntryType.Clearing:
                case LedgerEntryType.Purchase:
                    _logger.LogWarning(
                        "Installment plan {PlanId} on account {AccountId} was created before the deferred-principal model: original ledger entry {LedgerEntryId} is typed {LegacyType} and is reclassified to DeferredPrincipal in flight (amount {Amount})",
                        plan.Id, plan.AccountId, original.Id, original.Type, Math.Abs(original.Amount));
                    original.Type = LedgerEntryType.DeferredPrincipal;
                    original.Amount = Math.Abs(original.Amount);
                    healed++;
                    billable.Add(plan);
                    break;

                default:
                    _logger.LogError(
                        "Installment plan {PlanId} on account {AccountId} points to ledger entry {LedgerEntryId} of type {Type}, which cannot back deferred principal; its installments are not billed until reconciled",
                        plan.Id, plan.AccountId, original.Id, original.Type);
                    break;
            }
        }

        // The ledger queries that follow filter by Type in the store, so the reclassification must be
        // persisted before they run; otherwise the healed row would still be selected as a Clearing.
        // The heal is a self-contained, idempotent correction, so committing it ahead of the statement
        // is safe even if statement generation fails afterwards.
        if (healed > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return billable;
    }

    public async Task<StatementEntity> ApplyStatementPaymentAsync(Guid statementId, decimal amount, DateTimeOffset postedOn, CancellationToken ct)
    {
        if (amount <= 0) throw new InvalidOperationException("Payment amount must be > 0");

        var st = await _db.Statements.FirstOrDefaultAsync(x => x.Id == statementId, ct);
        if (st is null) throw new InvalidOperationException("Statement not found");

        // The payment settles this statement, but as ledger activity it belongs to the cycle it is
        // posted in: it stays unassigned so the next GenerateStatementAsync lists it and nets it
        // against the carried balance. Stamping it with the closed statement would hide it forever.
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = st.AccountId,
            Type = LedgerEntryType.Payment,
            Amount = -Math.Abs(amount),
            Description = "PAYMENT - Statement payment",
            PostedOn = postedOn,
            StatementId = null
        });

        // A legacy statement has no buckets yet; materialize them from the accrued figures before the
        // payment is recorded, otherwise the all-zero buckets below would read as "fully paid".
        _minPay.ApproximateLegacyBuckets(st);

        st.PaidAmount += amount;

        // Totals follow the buckets as they stand now. When the caller has already allocated the
        // payment (ApplyPaymentCommandHandler), this is the post-allocation remainder; a fully paid
        // statement therefore ends at zero instead of keeping its pre-payment totals.
        st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
        st.NewBalance = st.TotalPaymentDue;

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
    /// ADR-7: pure arithmetic delegated to Domain calculator; entity mutation stays here.
    /// </summary>
    public void ApplyClosingTotals(StatementEntity st)
    {
        // Delegate pure arithmetic to Domain calculator (primitives only — no EF types cross the boundary)
        var result = ClosingTotalsCalculator.Compute(
            interestAccrued: st.InterestAccrued,
            feesTotal: st.Fees,
            newBalance: st.NewBalance);

        // Apply computed values back to entity (mutation stays in service layer)
        st.InterestDue = result.InterestDue;
        st.FeesDue = result.FeesDue;
        st.PrincipalDue = result.PrincipalDue;
        st.TotalPaymentDue = result.TotalPaymentDue;
        st.NewBalance = result.NewBalance;
    }
}