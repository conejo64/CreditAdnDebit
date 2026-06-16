using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

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

    public decimal CalculateMinimum(StatementEntity st, MinimumPaymentPolicyEntity p)
    {
        // If older statements, initialize buckets approx (entity mutation stays in the service layer)
        if (st.PrincipalDue == 0 && st.InterestDue == 0 && st.FeesDue == 0)
        {
            st.InterestDue = st.InterestAccrued;
            st.FeesDue = st.LateFeeAmount;
            st.PrincipalDue = Math.Max(0, st.StatementBalance - st.InterestDue - st.FeesDue);
        }

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
