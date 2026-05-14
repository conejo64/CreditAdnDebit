using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

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
        var entry = await _db.LedgerEntries.FirstOrDefaultAsync(x => x.Id == ledgerEntryId && x.AccountId == accountId, ct)
            ?? throw new InvalidOperationException("Transaction not found");

        if (entry.Type != LedgerEntryType.Purchase && entry.Type != LedgerEntryType.Clearing)
            throw new InvalidOperationException("Only purchases or clearings can be deferred");

        if (entry.StatementId != null)
            throw new InvalidOperationException("Transaction already invoiced in a statement");

        // Check if already deferred
        var alreadyDeferred = await _db.InstallmentPlans.AnyAsync(x => x.OriginalLedgerEntryId == ledgerEntryId, ct);
        if (alreadyDeferred) throw new InvalidOperationException("Transaction already deferred");

        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found");

        var apr = customApr ?? 0.35m; // Default or from policy
        var dailyRate = apr / 365m;

        var plan = new InstallmentPlanEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TotalAmount = entry.Amount,
            TotalInstallments = installments,
            RemainingInstallments = installments,
            InterestApr = apr,
            Status = InstallmentPlanStatus.Active,
            Description = $"Diferido: {entry.Description}",
            OriginalLedgerEntryId = ledgerEntryId,
            CreatedOn = DateTimeOffset.UtcNow
        };

        // Create Amortization Schedule (Simplified Flat Interest or French system)
        // For credit cards, it's often flat principal + interest on balance
        decimal principalPerInstallment = Math.Round(entry.Amount / installments, 2);
        decimal lastPrincipalAdjustment = entry.Amount - (principalPerInstallment * (installments - 1));

        for (int i = 1; i <= installments; i++)
        {
            decimal principal = (i == installments) ? lastPrincipalAdjustment : principalPerInstallment;
            // Simplified: Interest calculated for 30 days of the remaining balance
            decimal remainingBalanceBefore = entry.Amount - (principalPerInstallment * (i - 1));
            decimal interest = Math.Round(remainingBalanceBefore * (apr / 12m), 2);

            plan.AmortizationSchedule.Add(new AmortizationScheduleEntity
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                InstallmentNumber = i,
                PrincipalAmount = principal,
                InterestAmount = interest,
                TotalInstallmentAmount = principal + interest,
                DueDate = DateTime.UtcNow.AddMonths(i),
                Status = InstallmentStatus.Pending,
                CreatedOn = DateTimeOffset.UtcNow
            });
        }

        _db.InstallmentPlans.Add(plan);

        // Update original entry to reflect it's being deferred (optional, but good for UX)
        // We could also "revert" the ledger entry amount to 0 and move it to a "Deferred" type
        // but it's better to keep it and just mark it.
        // For Zitron, we'll mark the original as "Deferred" so it doesn't count for the current balance
        // but the plan principal will impact the "Total Balance".
        // Actually, the simplest is to change the Type to help the Billing Engine skip it.
        entry.Type = LedgerEntryType.Clearing; // Keep it as clearing but the billing engine will now look for installments

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("billing.installment.created",
            new { accountId, ledgerEntryId, installments, total = entry.Amount, apr },
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
}
