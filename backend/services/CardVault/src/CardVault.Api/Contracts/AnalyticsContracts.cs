namespace CardVault.Api.Contracts;

public sealed record AnalyticsBreakdownItemView(
    string Key,
    decimal Amount,
    int Count,
    decimal SharePercent);

public sealed record AnalyticsTimeSeriesPointView(
    DateOnly Date,
    decimal Amount,
    int Count);

public sealed record AnalyticsPortfolioSummaryView(
    int Customers,
    int Accounts,
    int ActiveAccounts,
    int ActiveCards,
    decimal TotalCreditLimit,
    decimal AvailableCredit,
    decimal OutstandingBalance,
    decimal OpenStatementBalance,
    int OpenDisputeCount,
    decimal OpenDisputeAmount);

public sealed record ConsumptionAnalyticsView(
    int Days,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal GrossConsumptionAmount,
    decimal NetConsumptionAmount,
    int MovementCount,
    decimal AverageTicket,
    IReadOnlyList<AnalyticsBreakdownItemView> CategoryBreakdown,
    IReadOnlyList<AnalyticsBreakdownItemView> ProductBreakdown,
    IReadOnlyList<AnalyticsBreakdownItemView> NetworkBreakdown,
    IReadOnlyList<AnalyticsTimeSeriesPointView> DailyTrend);

public sealed record FraudAnalyticsView(
    int Days,
    DateOnly FromDate,
    DateOnly ToDate,
    int TotalCases,
    int OpenCases,
    int WonCases,
    int LostCases,
    decimal TotalExposureAmount,
    decimal OpenExposureAmount,
    decimal CasesPerThousandPurchases,
    IReadOnlyList<AnalyticsBreakdownItemView> NetworkBreakdown,
    IReadOnlyList<AnalyticsBreakdownItemView> ReasonCodeBreakdown,
    IReadOnlyList<AnalyticsBreakdownItemView> StatusBreakdown,
    IReadOnlyList<AnalyticsTimeSeriesPointView> OpenedTrend);

public sealed record BusinessAnalyticsDashboardView(
    AnalyticsPortfolioSummaryView Portfolio,
    ConsumptionAnalyticsView Consumption,
    FraudAnalyticsView Fraud);
