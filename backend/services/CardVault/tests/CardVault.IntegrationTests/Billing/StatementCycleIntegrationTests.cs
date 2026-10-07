using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Billing;

/// <summary>
/// Gate 0 / T4 on real PostgreSQL: open authorization holds are excluded from a statement's
/// previous balance and purchases, and a payment posted after the cut-off lands in the next cycle.
/// Mirrors <c>CardVault.Tests.Billing.HoldExclusionAndPaymentCycleTests</c>.
/// </summary>
public sealed class StatementCycleIntegrationTests : IntegrationTestBase
{
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Jan =
        (new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Feb =
        (new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 2, 28, 0, 0, 0, DateTimeKind.Utc));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Mar =
        (new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc));

    private BillingService _billing = null!;

    public StatementCycleIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    protected override void OnDbReady()
    {
        var audit = new AuditService(Db);
        var policies = new CreditPolicyService(Db);
        _billing = new BillingService(Db, new MinimumPaymentService(Db), policies, audit);
    }

    [Fact(DisplayName = "Statement previous balance and purchases exclude open holds; a captured hold counts once via Clearing")]
    public async Task Open_hold_is_excluded_from_statement_previous_balance()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 5000m, availableLimit: 4700m);
        AddEntry(account.Id, LedgerEntryType.Purchase, 100m, On(2024, 12, 20));           // previous balance
        AddEntry(account.Id, LedgerEntryType.AuthorizationHold, 50m, On(2024, 12, 28));   // open hold before the cycle
        AddEntry(account.Id, LedgerEntryType.AuthorizationHold, 30m, On(2025, 1, 10));    // open hold in the cycle
        AddEntry(account.Id, LedgerEntryType.AuthorizationHold, 40m, On(2025, 1, 11));    // captured hold ...
        AddEntry(account.Id, LedgerEntryType.Clearing, 40m, On(2025, 1, 12));             // ... and its clearing
        await Db.SaveChangesAsync();

        var st = await GenerateAsync(account.Id, Jan);

        st.PreviousBalance.Should().Be(100m, "the pre-cycle open hold is not posted debt");
        st.Purchases.Should().Be(40m, "only the clearing is a purchase; the hold behind it is not added again");
        st.NewBalance.Should().Be(140m);

        var lines = await _billing.GetLinesAsync(st.Id, CancellationToken.None);
        lines.Should().NotContain(l => l.Type == LedgerEntryType.AuthorizationHold, "pending authorizations are not statement lines");

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var holds = await reader.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == account.Id && x.Type == LedgerEntryType.AuthorizationHold)
            .ToListAsync();
        holds.Should().OnlyContain(h => h.StatementId == null, "shadow entries are never invoiced");
    }

    [Fact(DisplayName = "A payment posted after the cut-off is listed on the next statement and reduces its balance")]
    public async Task Payment_after_cutoff_lands_in_the_next_statement()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 5000m, availableLimit: 4700m);
        AddEntry(account.Id, LedgerEntryType.Purchase, 200m, On(2025, 1, 15));
        await Db.SaveChangesAsync();

        var jan = await GenerateAsync(account.Id, Jan);
        jan.NewBalance.Should().Be(200m);

        await _billing.ApplyStatementPaymentAsync(jan.Id, 50m, On(2025, 2, 5), CancellationToken.None);

        var feb = await GenerateAsync(account.Id, Feb);

        feb.PreviousBalance.Should().Be(200m);
        feb.Payments.Should().Be(-50m, "the payment belongs to the cycle it was posted in");
        feb.NewBalance.Should().Be(150m);

        var febLines = await _billing.GetLinesAsync(feb.Id, CancellationToken.None);
        febLines.Should().ContainSingle(l => l.Type == LedgerEntryType.Payment && l.Amount == -50m);

        var janLines = await _billing.GetLinesAsync(jan.Id, CancellationToken.None);
        janLines.Should().NotContain(l => l.Type == LedgerEntryType.Payment, "the closed statement does not change after the cut-off");

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var payment = await reader.LedgerEntries.AsNoTracking()
            .SingleAsync(x => x.AccountId == account.Id && x.Type == LedgerEntryType.Payment);
        payment.StatementId.Should().Be(feb.Id);

        var mar = await GenerateAsync(account.Id, Mar);
        mar.PreviousBalance.Should().Be(150m, "the payment is carried into every later cycle");
    }

    // Observed 2026-10-05 on postgres:16-alpine with HEAD d99e89f (Gate 0 / T6), before T4b:
    //   System.ArgumentException : Cannot write DateTime with Kind=Unspecified to PostgreSQL type
    //   'timestamp with time zone', only UTC is supported.
    //   at CardVault.Application.Services.BillingService.GenerateStatementAsync(...)
    // A JSON body with a date-only "2025-01-31" deserializes to Kind=Unspecified and reaches this
    // path through BillingCommands unchanged. BillingService now normalizes every incoming DateTime
    // to UTC at its boundary (calendar-date semantics for Unspecified, instant conversion for Local).
    [Fact(DisplayName = "A statement date with Kind=Unspecified is normalized to UTC by GenerateStatementAsync and persisted")]
    public async Task Statement_date_with_unspecified_kind_is_normalized()
    {
        var account = await SeedCreditAccountAsync(creditLimit: 5000m, availableLimit: 4700m);
        AddEntry(account.Id, LedgerEntryType.Purchase, 200m, On(2025, 1, 15));
        await Db.SaveChangesAsync();

        var unspecifiedStatementDate = new DateTime(2025, 1, 31);
        unspecifiedStatementDate.Kind.Should().Be(DateTimeKind.Unspecified);
        var unspecifiedCycleStart = new DateTime(2025, 1, 1);
        var unspecifiedCycleEnd = new DateTime(2025, 1, 31, 23, 59, 59);

        var st = await _billing.GenerateStatementAsync(account.Id, unspecifiedCycleStart, unspecifiedCycleEnd, unspecifiedStatementDate, dueDateOverride: null, CancellationToken.None);

        st.NewBalance.Should().Be(200m);

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var persisted = await reader.Statements.AsNoTracking().SingleAsync(x => x.Id == st.Id);
        persisted.StatementDate.Should().Be(new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc), "an unspecified date is the same calendar date in UTC");
        persisted.StatementDate.Kind.Should().Be(DateTimeKind.Utc);
        persisted.DueDate.Kind.Should().Be(DateTimeKind.Utc);
        persisted.CycleStart.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        persisted.CycleEnd.Should().Be(new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc));
    }

    private static DateTimeOffset On(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private void AddEntry(Guid accountId, LedgerEntryType type, decimal amount, DateTimeOffset postedOn) =>
        Db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = type.ToString().ToUpperInvariant(),
            PostedOn = postedOn
        });
}
