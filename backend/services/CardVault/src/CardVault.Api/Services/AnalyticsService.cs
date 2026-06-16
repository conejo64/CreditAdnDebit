using System.Diagnostics;
using CardVault.Api.Contracts;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Switch;
using Microsoft.EntityFrameworkCore;
using BillingDisputeStatus = CardVault.Infrastructure.Persistence.Billing.DisputeStatus;

namespace CardVault.Api.Services;

public sealed class AnalyticsService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public AnalyticsService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<BusinessAnalyticsDashboardView> GetDashboardAsync(int days, CancellationToken ct)
    {
        var normalizedDays = NormalizeDays(days, 30);
        var portfolio = await BuildPortfolioSummaryAsync(ct);
        var consumption = await BuildConsumptionAsync(normalizedDays, ct);
        var fraud = await BuildFraudAsync(normalizedDays, ct);

        await AuditAccessAsync("analytics.dashboard.viewed", normalizedDays, ct);
        return new BusinessAnalyticsDashboardView(portfolio, consumption, fraud);
    }

    public async Task<ConsumptionAnalyticsView> GetConsumptionAsync(int days, CancellationToken ct)
    {
        var normalizedDays = NormalizeDays(days, 30);
        var report = await BuildConsumptionAsync(normalizedDays, ct);
        await AuditAccessAsync("analytics.consumption.viewed", normalizedDays, ct);
        return report;
    }

    public async Task<FraudAnalyticsView> GetFraudAsync(int days, CancellationToken ct)
    {
        var normalizedDays = NormalizeDays(days, 90);
        var report = await BuildFraudAsync(normalizedDays, ct);
        await AuditAccessAsync("analytics.fraud.viewed", normalizedDays, ct);
        return report;
    }

    private async Task<AnalyticsPortfolioSummaryView> BuildPortfolioSummaryAsync(CancellationToken ct)
    {
        var customers = await _db.Customers.AsNoTracking().CountAsync(ct);
        var accounts = await _db.Accounts.AsNoTracking().CountAsync(ct);
        var activeAccounts = await _db.Accounts.AsNoTracking().CountAsync(x => x.Status == AccountStatus.Active, ct);
        var activeCards = await _db.Cards.AsNoTracking().CountAsync(x => x.Status == CardStatus.Active, ct);

        var creditAccounts = _db.Accounts.AsNoTracking().Where(x => x.AccountType == AccountType.Credit);
        var totalCreditLimit = await creditAccounts.SumAsync(x => (decimal?)x.CreditLimit, ct) ?? 0m;
        var availableCredit = await creditAccounts.SumAsync(x => (decimal?)x.AvailableLimit, ct) ?? 0m;
        var outstandingBalance = await creditAccounts.SumAsync(x => (decimal?)x.LedgerBalance, ct) ?? 0m;
        var openStatementBalance = await _db.Statements.AsNoTracking()
            .Where(x => x.Status == StatementStatus.Open)
            .SumAsync(x => (decimal?)x.NewBalance, ct) ?? 0m;

        var openDisputeStatuses = new[]
        {
            BillingDisputeStatus.Open,
            BillingDisputeStatus.Representment,
            BillingDisputeStatus.PreArbitration,
            BillingDisputeStatus.Arbitration
        };

        var openDisputes = await _db.DisputeCases.AsNoTracking()
            .Where(x => openDisputeStatuses.Contains(x.Status))
            .ToListAsync(ct);

        return new AnalyticsPortfolioSummaryView(
            customers,
            accounts,
            activeAccounts,
            activeCards,
            decimal.Round(totalCreditLimit, 2),
            decimal.Round(availableCredit, 2),
            decimal.Round(outstandingBalance, 2),
            decimal.Round(openStatementBalance, 2),
            openDisputes.Count,
            decimal.Round(openDisputes.Sum(x => Math.Abs(x.OriginalAmount)), 2));
    }

    private async Task<ConsumptionAnalyticsView> BuildConsumptionAsync(int days, CancellationToken ct)
    {
        var (fromDate, toDate, cutoff) = BuildWindow(days);

        var entries = await _db.LedgerEntries
            .AsNoTracking()
            .Where(x => x.PostedOn >= cutoff && x.Type != LedgerEntryType.Payment && x.Type != LedgerEntryType.AuthorizationHold)
            .Select(x => new ConsumptionLedgerRow(x.AccountId, x.Type, x.Amount, x.PostedOn))
            .ToListAsync(ct);

        var accountIds = entries.Select(x => x.AccountId).Distinct().ToList();
        var productCodes = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts.AsNoTracking()
                .Where(x => accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.ProductCode })
                .ToDictionaryAsync(x => x.Id, x => x.ProductCode, ct);

        var networkRows = await _db.TxnJournal
            .AsNoTracking()
            .Where(x => x.PostedOn >= cutoff &&
                        x.TxnType != SwitchTxnType.Authorization)
            .Select(x => new ConsumptionNetworkRow(x.Network, x.Amount))
            .ToListAsync(ct);

        var grossAmount = entries.Sum(x => Math.Abs(x.Amount));
        var netAmount = entries.Sum(x => x.Amount);
        var movementCount = entries.Count;
        var averageTicket = movementCount == 0 ? 0m : grossAmount / movementCount;

        var categoryBreakdown = BuildBreakdown(
            entries.GroupBy(x => x.Type.ToString().ToUpperInvariant())
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => x.Amount), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var productBreakdown = BuildBreakdown(
            entries.GroupBy(x => productCodes.TryGetValue(x.AccountId, out var productCode) && !string.IsNullOrWhiteSpace(productCode)
                    ? productCode
                    : "UNASSIGNED")
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => x.Amount), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var networkBreakdown = BuildBreakdown(
            networkRows.GroupBy(x => string.IsNullOrWhiteSpace(x.Network) ? "UNKNOWN" : x.Network.Trim().ToUpperInvariant())
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => Math.Abs(x.Amount)), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var byDate = entries
            .GroupBy(x => DateOnly.FromDateTime(x.PostedOn.UtcDateTime.Date))
            .ToDictionary(g => g.Key, g => new AnalyticsTimeSeriesPointView(
                g.Key,
                decimal.Round(g.Sum(x => x.Amount), 2),
                g.Count()));

        var dailyTrend = EnumerateDays(fromDate, toDate)
            .Select(date => byDate.TryGetValue(date, out var point)
                ? point
                : new AnalyticsTimeSeriesPointView(date, 0m, 0))
            .ToList();

        return new ConsumptionAnalyticsView(
            days,
            fromDate,
            toDate,
            decimal.Round(grossAmount, 2),
            decimal.Round(netAmount, 2),
            movementCount,
            movementCount == 0 ? 0m : decimal.Round(averageTicket, 2),
            categoryBreakdown,
            productBreakdown,
            networkBreakdown,
            dailyTrend);
    }

    private async Task<FraudAnalyticsView> BuildFraudAsync(int days, CancellationToken ct)
    {
        var (fromDate, toDate, cutoff) = BuildWindow(days);

        var openDisputeStatuses = new[]
        {
            BillingDisputeStatus.Open,
            BillingDisputeStatus.Representment,
            BillingDisputeStatus.PreArbitration,
            BillingDisputeStatus.Arbitration
        };

        var disputes = await _db.DisputeCases
            .AsNoTracking()
            .Where(x => x.OpenedOn >= cutoff)
            .Select(x => new FraudDisputeRow(x.Network, x.ReasonCode, x.Status, x.OriginalAmount, x.OpenedOn))
            .ToListAsync(ct);

        var purchaseCount = await _db.TxnJournal.AsNoTracking()
            .CountAsync(x => x.PostedOn >= cutoff && x.TxnType == SwitchTxnType.Purchase, ct);

        var totalCases = disputes.Count;
        var openCases = disputes.Count(x => openDisputeStatuses.Contains(x.Status));
        var wonCases = disputes.Count(x => x.Status == BillingDisputeStatus.Won);
        var lostCases = disputes.Count(x => x.Status == BillingDisputeStatus.Lost);
        var totalExposure = disputes.Sum(x => Math.Abs(x.Amount));
        var openExposure = disputes.Where(x => openDisputeStatuses.Contains(x.Status)).Sum(x => Math.Abs(x.Amount));
        var casesPerThousandPurchases = purchaseCount == 0 ? 0m : (decimal)totalCases * 1000m / purchaseCount;

        var networkBreakdown = BuildBreakdown(
            disputes.GroupBy(x => string.IsNullOrWhiteSpace(x.Network) ? "UNKNOWN" : x.Network.Trim().ToUpperInvariant())
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => Math.Abs(x.Amount)), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var reasonBreakdown = BuildBreakdown(
            disputes.GroupBy(x => string.IsNullOrWhiteSpace(x.ReasonCode) ? "UNSPECIFIED" : x.ReasonCode.Trim().ToUpperInvariant())
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => Math.Abs(x.Amount)), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var statusBreakdown = BuildBreakdown(
            disputes.GroupBy(x => x.Status.ToString().ToUpperInvariant())
                .Select(g => new BreakdownSeed(g.Key, g.Sum(x => Math.Abs(x.Amount)), g.Count(), g.Sum(x => Math.Abs(x.Amount))))
                .OrderByDescending(x => x.GrossAmount));

        var openedTrendLookup = disputes
            .GroupBy(x => DateOnly.FromDateTime(x.OpenedOn.UtcDateTime.Date))
            .ToDictionary(g => g.Key, g => new AnalyticsTimeSeriesPointView(
                g.Key,
                decimal.Round(g.Sum(x => Math.Abs(x.Amount)), 2),
                g.Count()));

        var openedTrend = EnumerateDays(fromDate, toDate)
            .Select(date => openedTrendLookup.TryGetValue(date, out var point)
                ? point
                : new AnalyticsTimeSeriesPointView(date, 0m, 0))
            .ToList();

        return new FraudAnalyticsView(
            days,
            fromDate,
            toDate,
            totalCases,
            openCases,
            wonCases,
            lostCases,
            decimal.Round(totalExposure, 2),
            decimal.Round(openExposure, 2),
            decimal.Round(casesPerThousandPurchases, 2),
            networkBreakdown,
            reasonBreakdown,
            statusBreakdown,
            openedTrend);
    }

    private async Task AuditAccessAsync(string eventType, int days, CancellationToken ct)
    {
        await _audit.WriteAsync(
            eventType,
            new
            {
                days,
                traceId = Activity.Current?.TraceId.ToString()
            },
            correlationId: null,
            traceId: Activity.Current?.TraceId.ToString(),
            ct);
    }

    private static IReadOnlyList<AnalyticsBreakdownItemView> BuildBreakdown(IEnumerable<BreakdownSeed> seeds)
    {
        var items = seeds.ToList();
        var totalGross = items.Sum(x => x.GrossAmount);

        return items.Select(x => new AnalyticsBreakdownItemView(
                x.Key,
                decimal.Round(x.Amount, 2),
                x.Count,
                totalGross == 0m ? 0m : decimal.Round((x.GrossAmount / totalGross) * 100m, 2)))
            .ToList();
    }

    private static IEnumerable<DateOnly> EnumerateDays(DateOnly fromDate, DateOnly toDate)
    {
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            yield return date;
    }

    private static (DateOnly fromDate, DateOnly toDate, DateTimeOffset cutoff) BuildWindow(int days)
    {
        var toDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var fromDate = toDate.AddDays(-(days - 1));
        var cutoff = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (fromDate, toDate, cutoff);
    }

    private static int NormalizeDays(int days, int fallback)
        => days <= 0 ? fallback : Math.Min(days, 365);

    private sealed record BreakdownSeed(string Key, decimal Amount, int Count, decimal GrossAmount);
    private sealed record ConsumptionLedgerRow(Guid AccountId, LedgerEntryType Type, decimal Amount, DateTimeOffset PostedOn);
    private sealed record ConsumptionNetworkRow(string Network, decimal Amount);
    private sealed record FraudDisputeRow(string Network, string ReasonCode, BillingDisputeStatus Status, decimal Amount, DateTimeOffset OpenedOn);
}
