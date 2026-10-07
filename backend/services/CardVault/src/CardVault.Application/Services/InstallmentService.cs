using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

/// <summary>
/// Converts an unbilled purchase into an installment plan.
///
/// Ledger mechanism: the original entry is reclassified to <see cref="LedgerEntryType.DeferredPrincipal"/>.
/// It keeps its amount, so the account exposure (available credit) is unchanged, but statement
/// balances and daily interest skip it. <see cref="BillingService"/> later moves each due installment
/// out of that bucket (negative <see cref="LedgerEntryType.DeferredPrincipal"/>) into a billable
/// <see cref="LedgerEntryType.Installment"/> debit, so at every point in time
/// original purchase == remaining deferred principal + billed installments.
///
/// APR: taken from the request when supplied, otherwise from the product's
/// <c>DefaultInstallmentApr</c>; either way it must not exceed the product's optional
/// <c>MaxInstallmentApr</c>. There is no hard-coded fallback rate.
/// </summary>
public sealed class InstallmentService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public InstallmentService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<InstallmentPlanEntity> DeferPurchaseAsync(Guid accountId, Guid ledgerEntryId, int installments, decimal? customApr, CancellationToken ct)
    {
        if (installments < 1)
            throw new InvalidOperationException("An installment plan needs at least one installment");

        var entry = await _db.LedgerEntries.FirstOrDefaultAsync(x => x.Id == ledgerEntryId && x.AccountId == accountId, ct)
            ?? throw new InvalidOperationException("Transaction not found");

        if (entry.Type != LedgerEntryType.Purchase && entry.Type != LedgerEntryType.Clearing)
            throw new InvalidOperationException("Only purchases or clearings can be deferred");

        if (entry.StatementId != null)
            throw new InvalidOperationException("Transaction already invoiced in a statement");

        var alreadyDeferred = await _db.InstallmentPlans.AnyAsync(x => x.OriginalLedgerEntryId == ledgerEntryId, ct);
        if (alreadyDeferred) throw new InvalidOperationException("Transaction already deferred");

        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found");

        var apr = await ResolveAprAsync(acc.ProductCode, customApr, ct);

        var principal = Math.Abs(entry.Amount);

        var plan = new InstallmentPlanEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TotalAmount = principal,
            TotalInstallments = installments,
            RemainingInstallments = installments,
            InterestApr = apr,
            Status = InstallmentPlanStatus.Active,
            Description = $"Diferido: {entry.Description}",
            OriginalLedgerEntryId = ledgerEntryId,
            CreatedOn = DateTimeOffset.UtcNow
        };

        // Flat principal per installment, interest on the outstanding balance for one month.
        // Installments fall due monthly from the purchase posting date so billing is deterministic.
        decimal principalPerInstallment = Math.Round(principal / installments, 2);
        decimal lastPrincipalAdjustment = principal - (principalPerInstallment * (installments - 1));

        for (int i = 1; i <= installments; i++)
        {
            decimal installmentPrincipal = (i == installments) ? lastPrincipalAdjustment : principalPerInstallment;
            decimal remainingBalanceBefore = principal - (principalPerInstallment * (i - 1));
            decimal interest = Math.Round(remainingBalanceBefore * (apr / 12m), 2);

            plan.AmortizationSchedule.Add(new AmortizationScheduleEntity
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                InstallmentNumber = i,
                PrincipalAmount = installmentPrincipal,
                InterestAmount = interest,
                TotalInstallmentAmount = installmentPrincipal + interest,
                DueDate = entry.PostedOn.AddMonths(i).UtcDateTime,
                Status = InstallmentStatus.Pending,
                CreatedOn = DateTimeOffset.UtcNow
            });
        }

        _db.InstallmentPlans.Add(plan);

        // Park the principal: still owed and still consuming credit line, no longer billable
        // until each installment is released by the billing engine.
        entry.Type = LedgerEntryType.DeferredPrincipal;
        entry.Amount = principal;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.installment.created",
            new { accountId, ledgerEntryId, installments, total = principal, apr },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return plan;
    }

    public async Task<List<InstallmentPlanEntity>> GetActivePlansAsync(Guid accountId, CancellationToken ct)
    {
        return await _db.InstallmentPlans
            .Include(x => x.AmortizationSchedule)
            .Where(x => x.AccountId == accountId && x.Status == InstallmentPlanStatus.Active)
            .ToListAsync(ct);
    }

    private async Task<decimal> ResolveAprAsync(string productCode, decimal? requestedApr, CancellationToken ct)
    {
        var product = await _db.CardProducts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == productCode, ct)
            ?? throw new InvalidOperationException($"Card product '{productCode}' not found; the installment APR cannot be resolved");

        var apr = requestedApr ?? product.DefaultInstallmentApr
            ?? throw new InvalidOperationException($"No installment APR was requested and product '{productCode}' has no default installment APR configured");

        if (apr < 0m)
            throw new InvalidOperationException("Installment APR cannot be negative");

        if (product.MaxInstallmentApr is { } cap && apr > cap)
            throw new InvalidOperationException($"Requested installment APR {apr:P2} exceeds the maximum {cap:P2} configured for product '{productCode}'");

        return apr;
    }
}
