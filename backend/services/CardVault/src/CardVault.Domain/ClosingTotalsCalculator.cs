namespace CardVault.Domain;

/// <summary>
/// Result of the closing-totals formula (ADR-6 / ADR-7).
/// All values are computed from primitives; the caller applies them back to the entity.
/// </summary>
public sealed record ClosingTotalsResult(
    decimal InterestDue,
    decimal FeesDue,
    decimal PrincipalDue,
    decimal TotalPaymentDue,
    decimal NewBalance);

/// <summary>
/// Pure closing-totals arithmetic extracted from BillingService.ApplyClosingTotals.
/// Single source of truth for the terminal bucket-to-totals formula used by both
/// GenerateStatementAsync and SwitchTxnConsumer.UpdateOpenStatementAsync.
/// Takes only primitives; has no dependency on EF entities or Infrastructure.
/// </summary>
public static class ClosingTotalsCalculator
{
    /// <summary>
    /// Computes the closing bucket totals from the statement's interest, fees, and balance.
    /// </summary>
    /// <param name="interestAccrued">Total interest accrued in the cycle.</param>
    /// <param name="feesTotal">Total fees in the cycle (st.Fees).</param>
    /// <param name="newBalance">The running new balance before bucket assignment.</param>
    /// <returns>A <see cref="ClosingTotalsResult"/> with all computed bucket values.</returns>
    public static ClosingTotalsResult Compute(
        decimal interestAccrued,
        decimal feesTotal,
        decimal newBalance)
    {
        var interestDue = interestAccrued;
        // v40 - FeesDue includes all fees in cycle (overlimit/annual/cash-advance/late fees)
        var feesDue = feesTotal;
        var principalDue = Math.Max(0, newBalance - interestDue - feesDue);
        var totalPaymentDue = principalDue + interestDue + feesDue;
        var newBalanceFinal = totalPaymentDue;

        return new ClosingTotalsResult(interestDue, feesDue, principalDue, totalPaymentDue, newBalanceFinal);
    }
}
