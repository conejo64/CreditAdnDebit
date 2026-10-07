using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Ledger;

/// <summary>
/// Gate 0 / T2 on real PostgreSQL: credit-type entries (refund, reversal, chargeback, payment)
/// are stored negative and reduce the posted balance; the sum is computed by the database.
/// Mirrors <c>CardVault.Tests.Services.LedgerServiceTests</c>.
/// </summary>
public sealed class LedgerSignIntegrationTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private LedgerService _sut = null!;

    public LedgerSignIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    protected override void OnDbReady()
    {
        var audit = new AuditService(Db);
        _sut = new LedgerService(Db, audit, new AccountingService(Db, audit));
    }

    [Fact(DisplayName = "Purchase 100 then refund 40 leaves a posted balance of 60")]
    public async Task Refund_keeps_credit_sign_and_reduces_posted_balance()
    {
        var account = await SeedCreditAccountAsync();

        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var refund = await _sut.AddEntryAsync(account.Id, LedgerEntryType.Refund, 40m, "REFUND", Now.AddMinutes(1), CancellationToken.None);

        refund.Amount.Should().Be(-40m, "a refund is a credit and must be stored negative even when passed positive");
        (await _sut.GetBalanceAsync(account.Id, CancellationToken.None)).Should().Be(60m);

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var stored = await reader.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == refund.Id);
        stored.Amount.Should().Be(-40m, "the sign must survive the round trip through numeric");
    }

    [Fact(DisplayName = "Purchase 100 then reversal 100 nets the posted balance to zero")]
    public async Task Reversal_keeps_credit_sign_and_nets_to_zero()
    {
        var account = await SeedCreditAccountAsync();

        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var reversal = await _sut.AddEntryAsync(account.Id, LedgerEntryType.Reversal, -100m, "REVERSAL", Now.AddMinutes(1), CancellationToken.None);

        reversal.Amount.Should().Be(-100m);
        (await _sut.GetBalanceAsync(account.Id, CancellationToken.None)).Should().Be(0m);
    }

    [Fact(DisplayName = "Purchase 100 then chargeback 100 nets the posted balance to zero")]
    public async Task Chargeback_keeps_credit_sign_and_nets_to_zero()
    {
        var account = await SeedCreditAccountAsync();

        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var chargeback = await _sut.AddEntryAsync(account.Id, LedgerEntryType.Chargeback, 100m, "CHARGEBACK", Now.AddMinutes(1), CancellationToken.None);

        chargeback.Amount.Should().Be(-100m);
        (await _sut.GetBalanceAsync(account.Id, CancellationToken.None)).Should().Be(0m);
    }

    [Fact(DisplayName = "Payment stays negative and reduces the posted balance")]
    public async Task Payment_stays_negative()
    {
        var account = await SeedCreditAccountAsync();

        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var payment = await _sut.AddEntryAsync(account.Id, LedgerEntryType.Payment, 30m, "PAYMENT", Now.AddMinutes(1), CancellationToken.None);

        payment.Amount.Should().Be(-30m);
        (await _sut.GetBalanceAsync(account.Id, CancellationToken.None)).Should().Be(70m);
    }

    [Fact(DisplayName = "Fractional amounts keep their exact decimal value through PostgreSQL numeric")]
    public async Task Decimal_amounts_round_trip_exactly()
    {
        var account = await SeedCreditAccountAsync();

        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 33.33m, "PURCHASE", Now, CancellationToken.None);
        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 33.33m, "PURCHASE", Now.AddMinutes(1), CancellationToken.None);
        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Purchase, 33.34m, "PURCHASE", Now.AddMinutes(2), CancellationToken.None);
        await _sut.AddEntryAsync(account.Id, LedgerEntryType.Refund, 0.01m, "REFUND", Now.AddMinutes(3), CancellationToken.None);

        (await _sut.GetBalanceAsync(account.Id, CancellationToken.None)).Should().Be(99.99m);
    }
}
