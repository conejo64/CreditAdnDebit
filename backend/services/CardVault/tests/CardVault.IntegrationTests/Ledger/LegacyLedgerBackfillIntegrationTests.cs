using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CardVault.IntegrationTests.Ledger;

/// <summary>
/// Gate 0 / T2b: the one-off SQL script <c>docs/runbooks/sql/cardvault-ledger-legacy-backfill.sql</c>
/// heals ledger rows written before the sign contract (T1/T2) and the deferred-principal model (T3):
/// <list type="number">
///   <item>legacy hold releases typed <c>Reversal</c> become negative <c>AuthorizationHold</c> shadows;</item>
///   <item>positive <c>Refund</c>/<c>Reversal</c>/<c>Chargeback</c> rows become negative;</item>
///   <item>the original entry of an active plan still typed <c>Clearing</c> becomes <c>DeferredPrincipal</c>.</item>
/// </list>
/// The script is copied to the test output directory by the project file, run through Npgsql exactly
/// as <c>psql -f</c> would run it, and must be idempotent.
/// </summary>
public sealed class LegacyLedgerBackfillIntegrationTests : IntegrationTestBase
{
    private static readonly string ScriptPath =
        Path.Combine(AppContext.BaseDirectory, "Sql", "cardvault-ledger-legacy-backfill.sql");

    private static readonly DateTimeOffset PostedOn = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public LegacyLedgerBackfillIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    [Fact(DisplayName = "The backfill script heals every legacy shape once and is a no-op on the second run")]
    public async Task Backfill_heals_legacy_rows_and_is_idempotent()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 1000m);
        var seeded = await SeedLegacyDatasetAsync(account.Id);

        var firstRun = await RunScriptAsync();
        firstRun.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["hold_releases"] = 2,
            ["credit_signs"] = 3,
            ["deferred_principals"] = 1
        }, "every legacy row is touched exactly once");

        await using (var reader = PostgresFixture.CreateSiblingContext(Db))
        {
            var rows = await reader.LedgerEntries.AsNoTracking().Where(x => x.AccountId == account.Id).ToDictionaryAsync(x => x.Id);

            rows[seeded.ExpiredRelease].Type.Should().Be(LedgerEntryType.AuthorizationHold);
            rows[seeded.ExpiredRelease].Amount.Should().Be(-100m);
            rows[seeded.AuthRelease].Type.Should().Be(LedgerEntryType.AuthorizationHold);
            rows[seeded.AuthRelease].Amount.Should().Be(-50m, "a release that was stored positive is still a negative shadow");

            rows[seeded.Refund].Amount.Should().Be(-40m);
            rows[seeded.Reversal].Amount.Should().Be(-30m);
            rows[seeded.Chargeback].Amount.Should().Be(-20m);
            rows[seeded.Refund].Type.Should().Be(LedgerEntryType.Refund, "rule 1 only touches the sign");

            rows[seeded.AlreadyNegativeRefund].Amount.Should().Be(-10m, "rows already under the contract are untouched");

            rows[seeded.LegacyPlanOriginal].Type.Should().Be(LedgerEntryType.DeferredPrincipal);
            rows[seeded.LegacyPlanOriginal].Amount.Should().Be(300m);
            rows[seeded.HealthyPlanOriginal].Type.Should().Be(LedgerEntryType.DeferredPrincipal);
            rows[seeded.CompletedPlanOriginal].Type.Should().Be(LedgerEntryType.Clearing, "only active plans are healed");

            rows.Values.Where(x => x.Type == LedgerEntryType.AuthorizationHold).Sum(x => x.Amount)
                .Should().Be(0m, "both released holds net to zero");
        }

        var secondRun = await RunScriptAsync();
        secondRun.Values.Should().AllSatisfy(count => count.Should().Be(0), "the script is idempotent");
    }

    [Fact(DisplayName = "After the backfill the posted balance only carries real debt and the hold shadows net to zero")]
    public async Task Posted_balance_is_consistent_after_backfill()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 1000m);
        await SeedLegacyDatasetAsync(account.Id);

        await RunScriptAsync();

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var credit = await new AvailableCreditService(reader).GetAsync(account.Id, CancellationToken.None);

        // Clearing 100 + Clearing 60 (completed plan, untouched) + DeferredPrincipal 300 + 200
        // - Refund 40 - Reversal 30 - Chargeback 20 - Refund 10 = 560. Hold shadows are excluded.
        credit.PostedBalance.Should().Be(560m);
        credit.ActiveHolds.Should().Be(0m, "no hold is left active");
        credit.AvailableCredit.Should().Be(440m);
    }

    /// <summary>Executes the script and returns the per-rule row counts it reports through RAISE NOTICE.</summary>
    private async Task<Dictionary<string, int>> RunScriptAsync()
    {
        File.Exists(ScriptPath).Should().BeTrue($"the backfill script must be copied next to the test assembly ({ScriptPath})");
        var sql = await File.ReadAllTextAsync(ScriptPath);

        var counts = new Dictionary<string, int>();
        var connection = (NpgsqlConnection)Db.Database.GetDbConnection();

        void OnNotice(object? _, NpgsqlNoticeEventArgs e)
        {
            // Format emitted by the script: "backfill <rule>: <n> row(s)".
            var parts = e.Notice.MessageText.Split(':', 2);
            if (parts.Length == 2 && parts[0].StartsWith("backfill ", StringComparison.Ordinal))
            {
                var rule = parts[0]["backfill ".Length..].Trim();
                var number = new string(parts[1].TakeWhile(c => c == ' ' || char.IsDigit(c)).ToArray()).Trim();
                counts[rule] = int.Parse(number);
            }
        }

        connection.Notice += OnNotice;
        try
        {
            await Db.Database.ExecuteSqlRawAsync(sql);
        }
        finally
        {
            connection.Notice -= OnNotice;
        }

        return counts;
    }

    private sealed record LegacyDataset(
        Guid ExpiredRelease,
        Guid AuthRelease,
        Guid Refund,
        Guid Reversal,
        Guid Chargeback,
        Guid AlreadyNegativeRefund,
        Guid LegacyPlanOriginal,
        Guid HealthyPlanOriginal,
        Guid CompletedPlanOriginal);

    /// <summary>Writes rows exactly as pre-Gate 0 code left them, bypassing every service.</summary>
    private async Task<LegacyDataset> SeedLegacyDatasetAsync(Guid accountId)
    {
        LedgerEntryEntity Entry(LedgerEntryType type, decimal amount, string description) => new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = description,
            PostedOn = PostedOn
        };

        // Shape 2: two released holds. The placement is a positive AuthorizationHold (unchanged); the
        // old release was a Reversal, negative from HoldMaintenanceService and positive after the old
        // LedgerService flipped it.
        var hold1 = Entry(LedgerEntryType.AuthorizationHold, 100m, "AUTH HOLD VISA STAN:000001 RRN:000000000001");
        var expiredRelease = Entry(LedgerEntryType.Reversal, -100m, "HOLD EXPIRED VISA STAN:000001 RRN:000000000001");
        var hold2 = Entry(LedgerEntryType.AuthorizationHold, 50m, "AUTH HOLD VISA STAN:000002 RRN:000000000002");
        var authRelease = Entry(LedgerEntryType.Reversal, 50m, "AUTH RELEASE VISA MTI:0420 STAN:000002 RRN:000000000002");
        var captured = Entry(LedgerEntryType.Clearing, 100m, "CLEARING VISA STAN:000001");

        // Shape 1: credit types stored positive by the old Math.Abs, plus one already correct.
        var refund = Entry(LedgerEntryType.Refund, 40m, "REFUND - Store");
        var reversal = Entry(LedgerEntryType.Reversal, 30m, "REVERSAL VISA MTI:0400 STAN:000003");
        var chargeback = Entry(LedgerEntryType.Chargeback, 20m, "CHARGEBACK - Dispute");
        var alreadyNegative = Entry(LedgerEntryType.Refund, -10m, "REFUND - Already signed");

        // Shape 3: three plans. Active with Clearing original (legacy), active with DeferredPrincipal
        // original (healthy), completed with Clearing original (out of scope).
        var legacyOriginal = Entry(LedgerEntryType.Clearing, 300m, "CLEARING VISA STAN:000004");
        var healthyOriginal = Entry(LedgerEntryType.DeferredPrincipal, 200m, "CLEARING VISA STAN:000005");
        var completedOriginal = Entry(LedgerEntryType.Clearing, 60m, "CLEARING VISA STAN:000006");

        Db.LedgerEntries.AddRange(hold1, expiredRelease, hold2, authRelease, captured,
            refund, reversal, chargeback, alreadyNegative,
            legacyOriginal, healthyOriginal, completedOriginal);

        Db.InstallmentPlans.AddRange(
            Plan(accountId, legacyOriginal.Id, 300m, InstallmentPlanStatus.Active),
            Plan(accountId, healthyOriginal.Id, 200m, InstallmentPlanStatus.Active),
            Plan(accountId, completedOriginal.Id, 60m, InstallmentPlanStatus.Completed));

        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        return new LegacyDataset(
            expiredRelease.Id, authRelease.Id,
            refund.Id, reversal.Id, chargeback.Id, alreadyNegative.Id,
            legacyOriginal.Id, healthyOriginal.Id, completedOriginal.Id);
    }

    private static InstallmentPlanEntity Plan(Guid accountId, Guid originalId, decimal total, InstallmentPlanStatus status) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        TotalAmount = total,
        TotalInstallments = 3,
        RemainingInstallments = status == InstallmentPlanStatus.Completed ? 0 : 3,
        InterestApr = 0m,
        Status = status,
        Description = "Diferido: legacy",
        OriginalLedgerEntryId = originalId,
        CreatedOn = PostedOn
    };
}
