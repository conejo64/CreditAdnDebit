using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class BillingMaintenanceService
{
    private readonly CardVaultDbContext _db;
    private readonly MinimumPaymentService _minPay;
    private readonly CreditPolicyService _policies;
    private readonly AuditService _audit;

    public BillingMaintenanceService(CardVaultDbContext db, MinimumPaymentService minPay, CreditPolicyService policies, AuditService audit)
    {
        _db = db;
        _minPay = minPay;
        _policies = policies;
        _audit = audit;
    }

    public async Task<int> ApplyLateFeesForPastDueAsync(bool force, CancellationToken ct)
    {
        var today = DateTimeOffset.UtcNow.Date;

        var query = _db.Statements
            .Where(x => x.Status == StatementStatus.Open && x.LateFeeAppliedOn == null);

        if (!force)
            query = query.Where(x => x.DueDate < today);

        var list = await query.OrderBy(x => x.DueDate).Take(500).ToListAsync(ct);

        var applied = 0;

        foreach (var st in list)
        {
            // if paid minimum, skip (unless force)
            if (!force && st.PaidAmount >= st.MinimumPayment) continue;

            var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == st.AccountId, ct);
            if (acc is null) continue;

            var policy = await _policies.GetOrDefaultAsync(acc.ProductCode, ct);
            var fee = policy.LateFee;
            if (fee <= 0) continue;

            _db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = Guid.NewGuid(),
                AccountId = st.AccountId,
                Type = LedgerEntryType.Fee,
                Amount = Math.Abs(fee),
                Description = "FEE - Late payment",
                PostedOn = DateTimeOffset.UtcNow,
                StatementId = st.Id
            });

            st.LateFeeAppliedOn = DateTimeOffset.UtcNow;
            st.LateFeeAmount = fee;
            st.NewBalance += fee;
            st.TotalPaymentDue = st.NewBalance;

            applied++;
        }

        await _db.SaveChangesAsync(ct);

        if (applied > 0)
        {
            await _audit.WriteAsync("billing.late_fees.batch_applied",
                new { applied, force },
                correlationId: null,
                traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
                ct: ct);
        }

        return applied;
    }
}
