namespace CardVault.Domain;

/// <summary>
/// Pure minimum-payment arithmetic extracted from MinimumPaymentService.
/// Takes only primitive values; has no dependency on EF entities or Infrastructure.
/// </summary>
public static class MinimumPaymentCalculator
{
    /// <summary>
    /// Computes the minimum payment due given the resolved payment-allocation buckets
    /// and the policy parameters.
    /// </summary>
    /// <remarks>
    /// Callers are responsible for resolving the bucket values (PrincipalDue, InterestDue,
    /// FeesDue) before invoking this method. If the original entity buckets are all zero,
    /// the service layer must derive them from InterestAccrued, LateFeeAmount, and
    /// StatementBalance first, then pass the resolved values here.
    /// </remarks>
    /// <param name="principalDue">Resolved principal-due bucket.</param>
    /// <param name="interestDue">Resolved interest-due bucket.</param>
    /// <param name="feesDue">Resolved fees-due bucket.</param>
    /// <param name="floorAmount">Absolute minimum floor (e.g. 10.00).</param>
    /// <param name="principalPercent">Percent applied to principal due (e.g. 0.05 for 5%).</param>
    /// <param name="includeInterest">Whether interest is included in the minimum.</param>
    /// <param name="includeFees">Whether fees are included in the minimum.</param>
    /// <param name="ceilingAmount">Optional cap on the minimum payment.</param>
    /// <returns>The computed minimum payment, never exceeding totalDue.</returns>
    public static decimal Calculate(
        decimal principalDue,
        decimal interestDue,
        decimal feesDue,
        decimal floorAmount,
        decimal principalPercent,
        bool includeInterest,
        bool includeFees,
        decimal? ceilingAmount)
    {
        var totalDue = principalDue + interestDue + feesDue;
        if (totalDue <= 0) return 0;

        if (totalDue < floorAmount) return totalDue;

        var principalComponent = Math.Max(floorAmount, Math.Round(principalPercent * principalDue, 2, MidpointRounding.AwayFromZero));
        var interestComponent = includeInterest ? interestDue : 0m;
        var feesComponent = includeFees ? feesDue : 0m;

        var min = principalComponent + interestComponent + feesComponent;

        if (ceilingAmount.HasValue && ceilingAmount.Value > 0)
            min = Math.Min(min, ceilingAmount.Value);

        // never exceed total due
        return Math.Min(min, totalDue);
    }
}
