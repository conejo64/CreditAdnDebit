using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

/// <summary>
/// Allocates a payment across statement buckets following configured policy.
/// v37: Interest -> Fees -> Principal.
/// </summary>
public sealed class PaymentAllocatorService
{
    private readonly CardVaultDbContext _db;

    public PaymentAllocatorService(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<(decimal toInterest, decimal toFees, decimal toPrincipal)> AllocateAsync(Guid statementId, decimal paymentAmount, CancellationToken ct)
    {
        if (paymentAmount <= 0) return (0, 0, 0);

        var st = await _db.Statements.FirstOrDefaultAsync(x => x.Id == statementId, ct)
            ?? throw new InvalidOperationException("Statement not found");

        // Ensure bucket due fields are initialized for older statements
        if (st.PrincipalDue == 0 && st.InterestDue == 0 && st.FeesDue == 0)
        {
            // Principal due approximated as StatementBalance - InterestAccrued - LateFeeAmount (not perfect, demo)
            st.InterestDue = st.InterestAccrued;
            st.FeesDue = st.LateFeeAmount;
            st.PrincipalDue = Math.Max(0, st.StatementBalance - st.InterestDue - st.FeesDue);
        }

        decimal remaining = paymentAmount;

        decimal payInterest = Math.Min(remaining, Math.Max(0, st.InterestDue));
        remaining -= payInterest;
        st.InterestDue -= payInterest;
        st.PaidToInterest += payInterest;

        decimal payFees = Math.Min(remaining, Math.Max(0, st.FeesDue));
        remaining -= payFees;
        st.FeesDue -= payFees;
        st.PaidToFees += payFees;

        decimal payPrincipal = Math.Min(remaining, Math.Max(0, st.PrincipalDue));
        remaining -= payPrincipal;
        st.PrincipalDue -= payPrincipal;
        st.PaidToPrincipal += payPrincipal;

        // PaidAmount is handled by BillingService when posting payment.
        await _db.SaveChangesAsync(ct);

        return (payInterest, payFees, payPrincipal);
    }
}
