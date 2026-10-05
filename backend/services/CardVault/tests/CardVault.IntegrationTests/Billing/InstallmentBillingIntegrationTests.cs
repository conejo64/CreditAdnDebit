using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Billing;

/// <summary>
/// Gate 0 / T3 on real PostgreSQL: a 300 purchase deferred in 3 installments bills 100 per cycle,
/// never 400, and the ledger reconciles (exposure constant, principal moves from deferred to billed).
/// Mirrors <c>CardVault.Tests.Billing.InstallmentBillingTests</c>.
/// </summary>
public sealed class InstallmentBillingIntegrationTests : IntegrationTestBase
{
    private const string ProductCode = "VISA_CLASSIC";

    private static readonly DateTimeOffset PurchasePostedOn = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Jan =
        (new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Feb =
        (new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 2, 28, 0, 0, 0, DateTimeKind.Utc));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Mar =
        (new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Apr =
        (new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 4, 30, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 4, 30, 0, 0, 0, DateTimeKind.Utc));

    private BillingService _billing = null!;
    private InstallmentService _installments = null!;

    public InstallmentBillingIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    protected override void OnDbReady()
    {
        var audit = new AuditService(Db);
        var policies = new CreditPolicyService(Db);
        _billing = new BillingService(Db, new MinimumPaymentService(Db), policies, audit);
        _installments = new InstallmentService(Db, audit);
    }

    [Fact(DisplayName = "A 300 purchase deferred in 3 bills 100 on the next statement, not 400")]
    public async Task Deferred_purchase_is_billed_once_per_installment()
    {
        var accountId = await SeedProductAndAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        var jan = await GenerateAsync(accountId, Jan);
        jan.Purchases.Should().Be(0m, "the deferred purchase leaves the purchase base of the cycle it was posted in");
        jan.NewBalance.Should().Be(0m, "no installment is due yet in the purchase cycle");

        var feb = await GenerateAsync(accountId, Feb);
        feb.PreviousBalance.Should().Be(0m, "deferred principal is not carried as previous balance");
        feb.NewBalance.Should().Be(100m, "only the first installment is due");
        feb.TotalPaymentDue.Should().Be(100m);
        feb.PrincipalDue.Should().Be(100m);

        var febLines = await _billing.GetLinesAsync(feb.Id, CancellationToken.None);
        febLines.Should().ContainSingle(l => l.Type == LedgerEntryType.Installment && l.Amount == 100m);
    }

    [Fact(DisplayName = "Ledger reconciles: exposure stays 300 while principal moves from deferred to billed")]
    public async Task Ledger_reconciles_after_every_installment_is_billed()
    {
        var accountId = await SeedProductAndAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await GenerateAsync(accountId, Jan);
        await GenerateAsync(accountId, Feb);

        (await SumAsync(accountId)).Should().Be(300m, "the account exposure is unchanged by billing an installment");
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(200m, "two installments remain deferred");
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(100m, "one installment has been billed");

        await GenerateAsync(accountId, Mar);
        await GenerateAsync(accountId, Apr);

        (await SumAsync(accountId)).Should().Be(300m);
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(0m, "all principal has been billed");
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(300m);

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var reloaded = await reader.InstallmentPlans.AsNoTracking().FirstAsync(p => p.Id == plan.Id);
        reloaded.RemainingInstallments.Should().Be(0);
        reloaded.Status.Should().Be(InstallmentPlanStatus.Completed);
    }

    [Fact(DisplayName = "Plan interest is billed with the installment and lands in the statement interest bucket")]
    public async Task Plan_interest_is_billed_with_each_installment()
    {
        var accountId = await SeedProductAndAccountAsync(defaultApr: 0.12m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);
        plan.InterestApr.Should().Be(0.12m, "the APR comes from the product when the request does not supply one");

        await GenerateAsync(accountId, Jan);
        var feb = await GenerateAsync(accountId, Feb);

        feb.InterestDue.Should().Be(3.00m, "300 outstanding * 12 % / 12 months");
        feb.PrincipalDue.Should().Be(100m);
        feb.TotalPaymentDue.Should().Be(103.00m);
    }

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private async Task<decimal> SumAsync(Guid accountId, LedgerEntryType? type = null)
    {
        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        return await reader.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && (type == null || x.Type == type))
            .SumAsync(x => x.Amount);
    }

    private async Task<LedgerEntryEntity> AddPurchaseAsync(Guid accountId, decimal amount)
    {
        var entry = new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Purchase,
            Amount = amount,
            Description = "PURCHASE - Store",
            PostedOn = PurchasePostedOn
        };
        Db.LedgerEntries.Add(entry);
        await Db.SaveChangesAsync();
        return entry;
    }

    private async Task<Guid> SeedProductAndAccountAsync(decimal? defaultApr, decimal? maxApr = null)
    {
        Db.CardProducts.Add(new CardProductEntity
        {
            Code = ProductCode,
            Brand = "VISA",
            ProductType = "CREDIT",
            Name = "Visa Classic",
            Enabled = true,
            DefaultInstallmentApr = defaultApr,
            MaxInstallmentApr = maxApr
        });
        await Db.SaveChangesAsync();

        var account = await SeedCreditAccountAsync(creditLimit: 5000m, availableLimit: 4700m, productCode: ProductCode);
        return account.Id;
    }
}
