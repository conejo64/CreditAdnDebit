namespace CardVault.Domain;

/// <summary>
/// Pure average-daily-balance computation extracted from BillingService.
/// Takes only primitives; has no dependency on EF entities or Infrastructure.
/// </summary>
public static class AverageDailyBalanceCalculator
{
    /// <summary>
    /// Computes the average daily balance over the billing cycle.
    /// </summary>
    /// <param name="previousBalance">The running balance at the start of the cycle.</param>
    /// <param name="entries">
    /// Ordered list of (date, amount) pairs representing the ledger deltas within the cycle.
    /// Multiple entries on the same date are summed. Amounts follow sign convention:
    /// charges positive, payments negative.
    /// </param>
    /// <param name="cycleStart">First day of the billing cycle (inclusive).</param>
    /// <param name="cycleEnd">Last day of the billing cycle (inclusive).</param>
    /// <returns>
    /// The average daily balance rounded to 2 decimal places (AwayFromZero),
    /// or 0 if the cycle has zero or negative length.
    /// </returns>
    public static decimal Compute(
        decimal previousBalance,
        IReadOnlyList<(DateTime Date, decimal Amount)> entries,
        DateTime cycleStart,
        DateTime cycleEnd)
    {
        var start = cycleStart.Date;
        var end = cycleEnd.Date;
        var days = (end - start).Days + 1;
        if (days <= 0) return 0m;

        var byDate = new Dictionary<DateTime, decimal>();
        foreach (var (date, amount) in entries)
        {
            var d = date.Date;
            byDate.TryGetValue(d, out var existing);
            byDate[d] = existing + amount;
        }

        decimal running = previousBalance;
        decimal sum = 0m;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (byDate.TryGetValue(d, out var delta))
                running += delta;

            sum += running;
        }

        return Math.Round(sum / days, 2, MidpointRounding.AwayFromZero);
    }
}
