using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

/// <summary>
/// Credit-type ledger entries (refund, reversal, chargeback) must not be summed into the
/// statement's Purchases bucket. They are credits to the cardholder and belong with
/// payments, while still reducing the statement balance.
/// </summary>
public sealed class BillingServiceCreditBucketTests : IDisposable
{
    private static readonly DateTime CycleStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CycleEnd = new(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime StatementDate = new(2025, 1, 31);
    private static readonly DateTimeOffset PostedInCycle = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly CardVaultDbContext _db;
    private readonly BillingService _billing;

    public BillingServiceCreditBucketTests()
    {
        _db = TestDbContextFactory.Create();
        _billing = new BillingService(_db, new MinimumPaymentService(_db), new CreditPolicyService(_db), new AuditService(_db));
    }

    public void Dispose() => _db.Dispose();

    [Fact(DisplayName = "Statement purchases exclude refunds, reversals and chargebacks; balance still reflects them")]
    public async Task Credit_entries_are_not_counted_as_purchases()
    {
        var accountId = await SeedCreditAccountAsync();

        AddEntry(accountId, LedgerEntryType.Purchase, 200m);
        AddEntry(accountId, LedgerEntryType.Refund, -40m);
        AddEntry(accountId, LedgerEntryType.Reversal, -50m);
        AddEntry(accountId, LedgerEntryType.Chargeback, -10m);
        AddEntry(accountId, LedgerEntryType.Payment, -20m);
        await _db.SaveChangesAsync();

        var st = await _billing.GenerateStatementAsync(accountId, CycleStart, CycleEnd, StatementDate, dueDateOverride: null, CancellationToken.None);

        st.Purchases.Should().Be(200m, "only debit purchases belong in the purchases bucket");
        st.Payments.Should().Be(-120m, "payments and credits together reduce the balance");
        st.NewBalance.Should().Be(80m, "200 - 40 - 50 - 10 - 20");
    }

    private void AddEntry(Guid accountId, LedgerEntryType type, decimal amount) =>
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = type.ToString().ToUpperInvariant(),
            PostedOn = PostedInCycle
        });

    private async Task<Guid> SeedCreditAccountAsync()
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}",
            FullName = "Bucket Test Customer",
            DocumentId = "1234567890",
            Email = "bucket@test.com",
            Phone = "+1234567890",
            CreatedOn = DateTimeOffset.UtcNow
        });

        _db.Accounts.Add(new CardAccountEntity
        {
            Id = accountId,
            CustomerId = customerId,
            AccountNumber = $"ACC{accountId:N}",
            AccountType = AccountType.Credit,
            ProductCode = "VISA_CLASSIC",
            CreditLimit = 5000m,
            AvailableLimit = 4700m,
            CurrencyCode = "USD",
            Status = AccountStatus.Active,
            CreatedOn = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync();
        return accountId;
    }
}
