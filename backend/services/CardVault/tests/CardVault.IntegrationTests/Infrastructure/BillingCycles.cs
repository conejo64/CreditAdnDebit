using CardVault.Application.Services;
using CardVault.Infrastructure.Persistence.Billing;

namespace CardVault.IntegrationTests.Infrastructure;

/// <summary>A statement cycle expressed as UTC calendar bounds, the shape <c>BillingService.GenerateStatementAsync</c> takes.</summary>
public readonly record struct BillingCycle(DateTime Start, DateTime End, DateTime StatementDate)
{
    /// <summary>Whole calendar month: first day 00:00:00 to last day 23:59:59 UTC, statement dated the last day.</summary>
    public static BillingCycle Month(int year, int month)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        return new BillingCycle(
            new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(year, month, lastDay, 23, 59, 59, DateTimeKind.Utc),
            new DateTime(year, month, lastDay, 0, 0, 0, DateTimeKind.Utc));
    }
}

/// <summary>The 2025 cycles shared by the billing integration tests.</summary>
public static class BillingCycles
{
    public static readonly BillingCycle Jan = BillingCycle.Month(2025, 1);
    public static readonly BillingCycle Feb = BillingCycle.Month(2025, 2);
    public static readonly BillingCycle Mar = BillingCycle.Month(2025, 3);
    public static readonly BillingCycle Apr = BillingCycle.Month(2025, 4);
}

public static class BillingServiceTestExtensions
{
    public static Task<StatementEntity> GenerateAsync(this BillingService billing, Guid accountId, BillingCycle cycle) =>
        billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);
}
