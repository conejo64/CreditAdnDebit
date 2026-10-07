using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Billing;

/// <summary>
/// Deferred purchases must be billed exactly once: the original purchase leaves the statement's
/// purchase base when the plan is created, only the installment due in each cycle is billed,
/// daily interest accrues on billed installment principal only, and the installment APR comes
/// from product configuration with an optional cap.
///
/// Ledger mechanism under test: the original purchase is reclassified to
/// <see cref="LedgerEntryType.DeferredPrincipal"/> (stays in the account exposure), and every
/// billed installment posts an <see cref="LedgerEntryType.Installment"/> debit plus a negative
/// <see cref="LedgerEntryType.DeferredPrincipal"/> release, so the exposure never changes and
/// original purchase == remaining deferred principal + billed installments.
/// </summary>
public sealed class InstallmentBillingTests : IDisposable
{
    private const string ProductCode = "VISA_CLASSIC";

    private static readonly DateTimeOffset PurchasePostedOn = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Jan =
        (new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 1, 31));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Feb =
        (new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 2, 28));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Mar =
        (new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 3, 31));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Apr =
        (new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 4, 30, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 4, 30));

    private readonly CardVaultDbContext _db;
    private readonly BillingService _billing;
    private readonly InstallmentService _installments;
    private readonly DailyInterestAccrualService _accrual;

    public InstallmentBillingTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        var policies = new CreditPolicyService(_db);
        _billing = new BillingService(_db, new MinimumPaymentService(_db), policies, audit);
        _installments = new InstallmentService(_db, audit);
        _accrual = new DailyInterestAccrualService(_db, policies, audit);
    }

    public void Dispose() => _db.Dispose();

    // ── Acceptance 1: bill once ─────────────────────────────────────────────────

    [Fact(DisplayName = "A 300 purchase deferred in 3 bills 100 on the next statement, not 400")]
    public async Task Deferred_purchase_is_billed_once_per_installment()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        var janStatement = await GenerateAsync(accountId, Jan);
        janStatement.Purchases.Should().Be(0m, "the deferred purchase leaves the purchase base of the cycle it was posted in");
        janStatement.NewBalance.Should().Be(0m, "no installment is due yet in the purchase cycle");

        var febStatement = await GenerateAsync(accountId, Feb);
        febStatement.PreviousBalance.Should().Be(0m, "deferred principal is not carried as previous balance");
        febStatement.NewBalance.Should().Be(100m, "only the first installment is due");
        febStatement.TotalPaymentDue.Should().Be(100m);
        febStatement.PrincipalDue.Should().Be(100m);

        var febLines = await _billing.GetLinesAsync(febStatement.Id, CancellationToken.None);
        febLines.Should().ContainSingle(l => l.Type == LedgerEntryType.Installment && l.Amount == 100m);
    }

    [Fact(DisplayName = "Ledger reconciles: exposure stays 300 while principal moves from deferred to billed")]
    public async Task Ledger_reconciles_after_every_installment_is_billed()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
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

        var reloaded = await _db.InstallmentPlans.AsNoTracking().FirstAsync(p => p.Id == plan.Id);
        reloaded.RemainingInstallments.Should().Be(0);
        reloaded.Status.Should().Be(InstallmentPlanStatus.Completed);
    }

    [Fact(DisplayName = "Plan interest is billed with the installment and lands in the statement interest bucket")]
    public async Task Plan_interest_is_billed_with_each_installment()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.12m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);
        plan.InterestApr.Should().Be(0.12m, "the APR comes from the product when the request does not supply one");

        await GenerateAsync(accountId, Jan);
        var feb = await GenerateAsync(accountId, Feb);

        // 300 outstanding * 12 % / 12 months = 3.00 for the first installment
        feb.InterestDue.Should().Be(3.00m);
        feb.PrincipalDue.Should().Be(100m);
        feb.TotalPaymentDue.Should().Be(103.00m);

        var lines = await _billing.GetLinesAsync(feb.Id, CancellationToken.None);
        lines.Should().ContainSingle(l => l.Type == LedgerEntryType.Interest && l.Amount == 3.00m);
    }

    // ── Acceptance 2: interest only on billed principal ────────────────────────

    [Fact(DisplayName = "A 0 % deferred purchase accrues no daily interest before any installment is billed")]
    public async Task Zero_percent_plan_accrues_nothing_while_deferred()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        var created = await _accrual.AccrueAsync(accountId, new DateOnly(2025, 1, 16), new DateOnly(2025, 1, 31), CancellationToken.None);

        created.Should().Be(0, "there is no billable balance while the whole purchase is deferred");
        (await SumAsync(accountId, LedgerEntryType.Interest)).Should().Be(0m);
    }

    [Fact(DisplayName = "Daily interest accrues on the billed installment principal, not on the deferred remainder")]
    public async Task Daily_interest_base_is_the_billed_installment_only()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await GenerateAsync(accountId, Jan);
        await GenerateAsync(accountId, Feb); // bills installment 1 (100)

        await _accrual.AccrueAsync(accountId, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 1), CancellationToken.None);

        var record = await _db.InterestAccrualRecords.AsNoTracking()
            .SingleAsync(r => r.AccountId == accountId && r.AccrualDate == new DateOnly(2025, 3, 1));
        record.BalanceBase.Should().Be(100m, "only the billed installment is part of the revolving balance");
    }

    // ── Acceptance 3: APR from product configuration with cap ──────────────────

    [Fact(DisplayName = "An installment APR above the product cap is rejected")]
    public async Task Apr_above_product_cap_is_rejected()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.12m, maxApr: 0.20m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var act = () => _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: 0.25m, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        (await _db.InstallmentPlans.CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "An installment APR within the product cap is accepted")]
    public async Task Apr_within_product_cap_is_accepted()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.12m, maxApr: 0.20m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: 0.18m, CancellationToken.None);

        plan.InterestApr.Should().Be(0.18m);
    }

    [Fact(DisplayName = "Without a product default and without a requested APR the plan is rejected (no hard-coded rate)")]
    public async Task Missing_apr_configuration_is_rejected()
    {
        var accountId = await SeedAccountAsync(defaultApr: null);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var act = () => _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*APR*");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private Task<decimal> SumAsync(Guid accountId, LedgerEntryType? type = null) =>
        _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && (type == null || x.Type == type))
            .SumAsync(x => x.Amount);

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
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private async Task<Guid> SeedAccountAsync(decimal? defaultApr, decimal? maxApr = null)
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _db.CardProducts.Add(new CardProductEntity
        {
            Code = ProductCode,
            Brand = "VISA",
            ProductType = "CREDIT",
            Name = "Visa Classic",
            Enabled = true,
            DefaultInstallmentApr = defaultApr,
            MaxInstallmentApr = maxApr
        });

        _db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}",
            FullName = "Installment Test Customer",
            DocumentId = "1234567890",
            Email = "installments@test.com",
            Phone = "+1234567890",
            CreatedOn = DateTimeOffset.UtcNow
        });

        _db.Accounts.Add(new CardAccountEntity
        {
            Id = accountId,
            CustomerId = customerId,
            AccountNumber = $"ACC{accountId:N}",
            AccountType = AccountType.Credit,
            ProductCode = ProductCode,
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
