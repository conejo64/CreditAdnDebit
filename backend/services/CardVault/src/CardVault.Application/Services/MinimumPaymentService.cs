using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class MinimumPaymentService
{
    private readonly CardVaultDbContext _db;

    public MinimumPaymentService(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<MinimumPaymentPolicyEntity> GetDefaultAsync(CancellationToken ct)
    {
        var p = await _db.MinimumPaymentPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.IsDefault, ct);
        return p ?? new MinimumPaymentPolicyEntity { Code = "DEFAULT", IsDefault = true };
    }

    /// <summary>
    /// Older statements were generated without buckets: approximate them once from the accrued
    /// figures. A statement that has received a payment has real buckets, and all-zero there means
    /// fully paid, not legacy. "Received a payment" is read from both records: PaidAmount (set when
    /// the payment is posted) and PaidTo* (set when the allocator consumed the buckets), because the
    /// allocator runs before the payment is posted. Callers that are about to record a payment must
    /// invoke this BEFORE incrementing <see cref="StatementEntity.PaidAmount"/>.
    /// </summary>
    public void ApproximateLegacyBuckets(StatementEntity st)
    {
        var neverPaid = st.PaidAmount == 0 && st.PaidToPrincipal == 0 && st.PaidToInterest == 0 && st.PaidToFees == 0;
        if (st.PrincipalDue == 0 && st.InterestDue == 0 && st.FeesDue == 0 && neverPaid)
        {
            st.InterestDue = st.InterestAccrued;
            st.FeesDue = st.LateFeeAmount;
            st.PrincipalDue = Math.Max(0, st.StatementBalance - st.InterestDue - st.FeesDue);
        }
    }

    public decimal CalculateMinimum(StatementEntity st, MinimumPaymentPolicyEntity p)
    {
        ApproximateLegacyBuckets(st);

        // Delegate pure arithmetic to Domain calculator (primitives only — no EF types cross the boundary)
        return MinimumPaymentCalculator.Calculate(
            principalDue: st.PrincipalDue,
            interestDue: st.InterestDue,
            feesDue: st.FeesDue,
            floorAmount: p.FloorAmount,
            principalPercent: p.PrincipalPercent,
            includeInterest: p.IncludeInterest,
            includeFees: p.IncludeFees,
            ceilingAmount: p.CeilingAmount);
    }

    public async Task<StatementEntity> RecalculateAsync(Guid statementId, CancellationToken ct)
    {
        var st = await _db.Statements.FirstOrDefaultAsync(x => x.Id == statementId, ct)
            ?? throw new InvalidOperationException("Statement not found");

        // Update totals based on buckets
        st.TotalPaymentDue = st.PrincipalDue + st.InterestDue + st.FeesDue;
        st.NewBalance = st.TotalPaymentDue;

        var p = await GetDefaultAsync(ct);
        st.MinimumPayment = CalculateMinimum(st, p);

        await _db.SaveChangesAsync(ct);
        return st;
    }
}
