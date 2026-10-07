using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

/// <summary>
/// Behaviour tests for the ledger sign contract enforced by <see cref="LedgerService.AddEntryAsync"/>.
/// Debit types increase the cardholder's debt and are stored positive; credit types
/// (payments, refunds, reversals, chargebacks) reduce it and are stored negative,
/// whatever sign the caller passed.
/// </summary>
public sealed class LedgerServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly CardVaultDbContext _db;
    private readonly LedgerService _sut;

    public LedgerServiceTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        _sut = new LedgerService(_db, audit, new AccountingService(_db, audit));
    }

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedAccountAsync()
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Ledger Test Customer",
            DocumentId = $"DOC{Guid.NewGuid():N}"[..10],
            Email = "ledger@test.com",
            Phone = "+593999000004",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountType = AccountType.Credit,
            ProductCode = "TEST_PROD",
            CreditLimit = 1000m,
            AvailableLimit = 1000m,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..10],
            Status = AccountStatus.Active,
        }).Entity;

        await _db.SaveChangesAsync();
        return account.Id;
    }

    [Fact(DisplayName = "Purchase 100 then refund 40 leaves a posted balance of 60")]
    public async Task Refund_after_purchase_reduces_posted_balance()
    {
        var accountId = await SeedAccountAsync();

        await _sut.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var refund = await _sut.AddEntryAsync(accountId, LedgerEntryType.Refund, -40m, "REFUND", Now.AddMinutes(1), CancellationToken.None);

        refund.Amount.Should().Be(-40m, "a refund is a credit and must stay negative");
        (await _sut.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(60m);
    }

    [Fact(DisplayName = "Purchase 100 then reversal 100 nets the posted balance to zero")]
    public async Task Reversal_after_purchase_nets_to_zero()
    {
        var accountId = await SeedAccountAsync();

        await _sut.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var reversal = await _sut.AddEntryAsync(accountId, LedgerEntryType.Reversal, -100m, "REVERSAL", Now.AddMinutes(1), CancellationToken.None);

        reversal.Amount.Should().Be(-100m);
        (await _sut.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(0m);
    }

    [Fact(DisplayName = "Purchase 100 then chargeback 100 nets the posted balance to zero")]
    public async Task Chargeback_after_purchase_nets_to_zero()
    {
        var accountId = await SeedAccountAsync();

        await _sut.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var chargeback = await _sut.AddEntryAsync(accountId, LedgerEntryType.Chargeback, -100m, "CHARGEBACK", Now.AddMinutes(1), CancellationToken.None);

        chargeback.Amount.Should().Be(-100m);
        (await _sut.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(0m);
    }

    [Theory(DisplayName = "Credit types passed with a positive amount are normalized to negative")]
    [InlineData(LedgerEntryType.Payment)]
    [InlineData(LedgerEntryType.Refund)]
    [InlineData(LedgerEntryType.Reversal)]
    [InlineData(LedgerEntryType.Chargeback)]
    public async Task Credit_type_passed_positive_is_stored_negative(LedgerEntryType type)
    {
        var accountId = await SeedAccountAsync();

        var entry = await _sut.AddEntryAsync(accountId, type, 25m, type.ToString(), Now, CancellationToken.None);

        entry.Amount.Should().Be(-25m, "{0} is a credit to the cardholder", type);
    }

    [Theory(DisplayName = "Debit types passed with a negative amount are normalized to positive")]
    [InlineData(LedgerEntryType.Purchase)]
    [InlineData(LedgerEntryType.Fee)]
    [InlineData(LedgerEntryType.Interest)]
    [InlineData(LedgerEntryType.Clearing)]
    public async Task Debit_type_passed_negative_is_stored_positive(LedgerEntryType type)
    {
        var accountId = await SeedAccountAsync();

        var entry = await _sut.AddEntryAsync(accountId, type, -25m, type.ToString(), Now, CancellationToken.None);

        entry.Amount.Should().Be(25m, "{0} is a debit to the cardholder", type);
    }

    [Theory(DisplayName = "Signed types keep whatever sign the caller supplied")]
    [InlineData(LedgerEntryType.Adjustment, 15)]
    [InlineData(LedgerEntryType.Adjustment, -15)]
    [InlineData(LedgerEntryType.AuthorizationHold, 15)]
    [InlineData(LedgerEntryType.AuthorizationHold, -15)]
    public async Task Signed_type_keeps_caller_sign(LedgerEntryType type, int amount)
    {
        var accountId = await SeedAccountAsync();

        var entry = await _sut.AddEntryAsync(accountId, type, amount, type.ToString(), Now, CancellationToken.None);

        entry.Amount.Should().Be(amount);
    }

    [Fact(DisplayName = "Payment stays negative and reduces the posted balance")]
    public async Task Payment_stays_negative()
    {
        var accountId = await SeedAccountAsync();

        await _sut.AddEntryAsync(accountId, LedgerEntryType.Purchase, 100m, "PURCHASE", Now, CancellationToken.None);
        var payment = await _sut.AddEntryAsync(accountId, LedgerEntryType.Payment, 30m, "PAYMENT", Now.AddMinutes(1), CancellationToken.None);

        payment.Amount.Should().Be(-30m);
        (await _sut.GetBalanceAsync(accountId, CancellationToken.None)).Should().Be(70m);
    }

    [Fact(DisplayName = "LedgerEntryType classifies debit, credit and signed types")]
    public void LedgerEntryType_sign_classification()
    {
        LedgerEntryType.Purchase.IsDebit().Should().BeTrue();
        LedgerEntryType.Fee.IsDebit().Should().BeTrue();
        LedgerEntryType.Interest.IsDebit().Should().BeTrue();
        LedgerEntryType.Clearing.IsDebit().Should().BeTrue();

        LedgerEntryType.Payment.IsCredit().Should().BeTrue();
        LedgerEntryType.Refund.IsCredit().Should().BeTrue();
        LedgerEntryType.Reversal.IsCredit().Should().BeTrue();
        LedgerEntryType.Chargeback.IsCredit().Should().BeTrue();

        LedgerEntryType.Adjustment.IsDebit().Should().BeFalse();
        LedgerEntryType.Adjustment.IsCredit().Should().BeFalse();
        LedgerEntryType.AuthorizationHold.IsDebit().Should().BeFalse();
        LedgerEntryType.AuthorizationHold.IsCredit().Should().BeFalse();
    }
}
