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
/// Gate 0 / T3b. Edge cases of deferred-purchase billing around the invariant
/// original purchase == remaining deferred principal + billed installments:
/// rounding residue, what the purchase-cycle statement may and may not show, input guards,
/// overdue catch-up (two installments due in one cycle) and idempotent regeneration of a cycle.
/// </summary>
public sealed class InstallmentEdgeCaseTests : IDisposable
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

    public InstallmentEdgeCaseTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        var policies = new CreditPolicyService(_db);
        _billing = new BillingService(_db, new MinimumPaymentService(_db), policies, audit);
        _installments = new InstallmentService(_db, audit);
    }

    public void Dispose() => _db.Dispose();

    // ── 1. Rounding ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "100 in 3 installments bills 33.33 / 33.33 / 33.34 and leaves no deferred residue")]
    public async Task Rounding_leaves_no_residual_deferred_principal()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 100m);

        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);
        plan.AmortizationSchedule.OrderBy(s => s.InstallmentNumber).Select(s => s.PrincipalAmount)
            .Should().Equal(33.33m, 33.33m, 33.34m);

        await GenerateAsync(accountId, Jan);

        // Nothing is paid along the way, so each statement carries the previous installments forward.
        var feb = await GenerateAsync(accountId, Feb);
        (await InstallmentLineAsync(feb.Id)).Should().Be(33.33m);
        feb.NewBalance.Should().Be(33.33m);
        (await SumAsync(accountId)).Should().Be(100m, "exposure never drifts");
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(66.67m);

        var mar = await GenerateAsync(accountId, Mar);
        (await InstallmentLineAsync(mar.Id)).Should().Be(33.33m);
        mar.NewBalance.Should().Be(66.66m, "the unpaid first installment is carried as previous balance");
        (await SumAsync(accountId)).Should().Be(100m);
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(33.34m);

        var apr = await GenerateAsync(accountId, Apr);
        (await InstallmentLineAsync(apr.Id)).Should().Be(33.34m, "the last installment absorbs the rounding difference");
        apr.NewBalance.Should().Be(100.00m, "the three installments add up to exactly the purchase");
        (await SumAsync(accountId)).Should().Be(100m);
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(0m, "no cent is left parked");
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(100m);
    }

    // ── 2. Purchase-cycle statement ────────────────────────────────────────────

    [Fact(DisplayName = "The purchase-cycle statement shows no DeferredPrincipal line and does not stamp the reclassified entry")]
    public async Task Purchase_cycle_statement_has_no_deferred_principal_line()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        var jan = await GenerateAsync(accountId, Jan);

        var lines = await _billing.GetLinesAsync(jan.Id, CancellationToken.None);
        lines.Should().NotContain(l => l.Type == LedgerEntryType.DeferredPrincipal, "parked principal is not a statement line");
        lines.Should().NotContain(l => l.LedgerEntryId == purchase.Id, "the deferred purchase is billed through its installments, not as a line of the purchase cycle");

        var original = await _db.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == purchase.Id);
        original.Type.Should().Be(LedgerEntryType.DeferredPrincipal);
        original.StatementId.Should().BeNull("the reclassified entry is not invoiced in the purchase cycle");
    }

    // ── 3. Guards ──────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Fewer than one installment is rejected")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Fewer_than_one_installment_is_rejected(int installments)
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.12m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var act = () => _installments.DeferPurchaseAsync(accountId, purchase.Id, installments, customApr: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one installment*");
        (await _db.InstallmentPlans.CountAsync()).Should().Be(0);
        (await _db.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == purchase.Id)).Type.Should().Be(LedgerEntryType.Purchase, "a rejected plan leaves the purchase untouched");
    }

    [Fact(DisplayName = "A negative installment APR is rejected")]
    public async Task Negative_apr_is_rejected()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.12m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var act = () => _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: -0.05m, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*negative*");
        (await _db.InstallmentPlans.CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "A product whose default installment APR exceeds its own cap cannot create plans")]
    public async Task Product_default_above_cap_is_rejected()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0.30m, maxApr: 0.20m);
        var purchase = await AddPurchaseAsync(accountId, 300m);

        var act = () => _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        (await _db.InstallmentPlans.CountAsync()).Should().Be(0);
    }

    // ── 4. Overdue catch-up ────────────────────────────────────────────────────

    [Fact(DisplayName = "Two pending installments due by the cycle end are both billed, once each")]
    public async Task Two_installments_due_in_one_cycle_are_both_billed_once()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        var plan = await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await GenerateAsync(accountId, Jan);
        // February is skipped: installment 1 (due 15 Feb) and installment 2 (due 15 Mar) are both pending at the March close.
        var mar = await GenerateAsync(accountId, Mar);

        mar.PrincipalDue.Should().Be(200m);
        mar.NewBalance.Should().Be(200m);

        var lines = await _billing.GetLinesAsync(mar.Id, CancellationToken.None);
        lines.Where(l => l.Type == LedgerEntryType.Installment).Should().HaveCount(2).And.OnlyContain(l => l.Amount == 100m);

        var schedule = await _db.AmortizationSchedules.AsNoTracking().Where(s => s.PlanId == plan.Id).OrderBy(s => s.InstallmentNumber).ToListAsync();
        schedule.Take(2).Should().OnlyContain(s => s.Status == InstallmentStatus.Invoiced && s.BilledStatementId == mar.Id);
        schedule[2].Status.Should().Be(InstallmentStatus.Pending);

        (await SumAsync(accountId)).Should().Be(300m);
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(100m);
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(200m);
        (await _db.InstallmentPlans.AsNoTracking().SingleAsync(p => p.Id == plan.Id)).RemainingInstallments.Should().Be(1);
    }

    // ── 5. Idempotent regeneration ─────────────────────────────────────────────

    [Fact(DisplayName = "Regenerating a cycle on the same statement date is refused")]
    public async Task Regenerating_same_statement_date_is_refused()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await GenerateAsync(accountId, Jan);
        await GenerateAsync(accountId, Feb);

        var act = () => GenerateAsync(accountId, Feb);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already generated*");
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(100m);
    }

    [Fact(DisplayName = "A second statement over an already billed cycle bills no installment again")]
    public async Task Regenerating_same_cycle_bills_nothing_twice()
    {
        var accountId = await SeedAccountAsync(defaultApr: 0m);
        var purchase = await AddPurchaseAsync(accountId, 300m);
        await _installments.DeferPurchaseAsync(accountId, purchase.Id, installments: 3, customApr: null, CancellationToken.None);

        await GenerateAsync(accountId, Jan);
        var first = await GenerateAsync(accountId, Feb);
        first.NewBalance.Should().Be(100m);

        // Same cycle, different statement date (e.g. a late re-run): nothing left to bill.
        var second = await _billing.GenerateStatementAsync(accountId, Feb.Start, Feb.End, Feb.StatementDate.AddDays(1), dueDateOverride: null, CancellationToken.None);

        second.PrincipalDue.Should().Be(0m);
        var lines = await _billing.GetLinesAsync(second.Id, CancellationToken.None);
        lines.Should().NotContain(l => l.Type == LedgerEntryType.Installment);

        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(100m, "the installment was billed exactly once");
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(200m);
        (await SumAsync(accountId)).Should().Be(300m);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private async Task<decimal> InstallmentLineAsync(Guid statementId)
    {
        var lines = await _billing.GetLinesAsync(statementId, CancellationToken.None);
        return lines.Should().ContainSingle(l => l.Type == LedgerEntryType.Installment).Subject.Amount;
    }

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
            FullName = "Installment Edge Case Customer",
            DocumentId = "1234567890",
            Email = "installments-edge@test.com",
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
