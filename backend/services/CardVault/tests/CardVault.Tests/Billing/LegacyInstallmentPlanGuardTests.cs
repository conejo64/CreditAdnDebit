using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardVault.Tests.Billing;

/// <summary>
/// Gate 0 / T2b defensive guard. Installment plans created before the T3 ledger model left their
/// original entry typed <see cref="LedgerEntryType.Clearing"/> (or <see cref="LedgerEntryType.Purchase"/>).
/// Billing such a plan unchanged would post a negative <see cref="LedgerEntryType.DeferredPrincipal"/>
/// release with no positive counterpart and keep the original in the purchase base, so the principal
/// would be billed twice. <see cref="BillingService"/> must reclassify the original entry in flight
/// (idempotent, logged at Warning) before releasing any principal, and must leave alone a plan whose
/// original entry cannot be found.
/// </summary>
public sealed class LegacyInstallmentPlanGuardTests : IDisposable
{
    private const string ProductCode = "VISA_CLASSIC";

    private static readonly DateTimeOffset PurchasePostedOn = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Jan =
        (new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 1, 31));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Feb =
        (new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 2, 28));

    private readonly CardVaultDbContext _db;
    private readonly ListLogger<BillingService> _logger = new();
    private readonly BillingService _billing;

    public LegacyInstallmentPlanGuardTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        _billing = new BillingService(_db, new MinimumPaymentService(_db), new CreditPolicyService(_db), audit, _logger);
    }

    public void Dispose() => _db.Dispose();

    [Fact(DisplayName = "A legacy plan whose original entry is still Clearing is healed before its first installment is billed")]
    public async Task Legacy_clearing_original_is_reclassified_and_billed_once()
    {
        var accountId = await SeedAccountAsync();
        var original = await AddLegacyEntryAsync(accountId, LedgerEntryType.Clearing, 300m);
        var plan = await AddLegacyPlanAsync(accountId, original.Id, installments: 3);

        var jan = await GenerateAsync(accountId, Jan);
        jan.Purchases.Should().Be(0m, "the healed original leaves the purchase base of the cycle it was posted in");
        jan.NewBalance.Should().Be(0m, "nothing is due in the purchase cycle");

        var healed = await _db.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == original.Id);
        healed.Type.Should().Be(LedgerEntryType.DeferredPrincipal);
        healed.Amount.Should().Be(300m, "deferred principal is parked positive");

        var feb = await GenerateAsync(accountId, Feb);
        feb.PrincipalDue.Should().Be(100m, "exactly one installment is billed");

        var installments = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Type == LedgerEntryType.Installment).ToListAsync();
        installments.Should().ContainSingle().Which.Amount.Should().Be(100m);

        (await SumAsync(accountId)).Should().Be(300m, "the account exposure never changes");
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(200m, "the release has its positive counterpart");

        var invoiced = await _db.AmortizationSchedules.AsNoTracking().CountAsync(s => s.PlanId == plan.Id && s.Status == InstallmentStatus.Invoiced);
        invoiced.Should().Be(1);

        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains(original.Id.ToString()),
            "the in-flight heal is visible in the logs");
    }

    [Fact(DisplayName = "The in-flight heal is idempotent: a healed plan logs nothing on the next cycle")]
    public async Task Heal_runs_once_per_plan()
    {
        var accountId = await SeedAccountAsync();
        var original = await AddLegacyEntryAsync(accountId, LedgerEntryType.Clearing, 300m);
        await AddLegacyPlanAsync(accountId, original.Id, installments: 3);

        await GenerateAsync(accountId, Jan);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);

        await GenerateAsync(accountId, Feb);
        _logger.Entries.Where(e => e.Level == LogLevel.Warning).Should().HaveCount(1, "the original is already DeferredPrincipal");
    }

    [Fact(DisplayName = "A plan whose original entry cannot be found releases no principal")]
    public async Task Plan_without_original_entry_is_skipped()
    {
        var accountId = await SeedAccountAsync();
        var plan = await AddLegacyPlanAsync(accountId, originalLedgerEntryId: Guid.NewGuid(), installments: 3);

        await GenerateAsync(accountId, Jan);
        var feb = await GenerateAsync(accountId, Feb);

        feb.PrincipalDue.Should().Be(0m);
        (await SumAsync(accountId, LedgerEntryType.DeferredPrincipal)).Should().Be(0m, "no release without a counterpart");
        (await SumAsync(accountId, LedgerEntryType.Installment)).Should().Be(0m);

        var pending = await _db.AmortizationSchedules.AsNoTracking().CountAsync(s => s.PlanId == plan.Id && s.Status == InstallmentStatus.Pending);
        pending.Should().Be(3, "the schedule is left for a human to reconcile");

        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(plan.Id.ToString()));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private Task<decimal> SumAsync(Guid accountId, LedgerEntryType? type = null) =>
        _db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && (type == null || x.Type == type))
            .SumAsync(x => x.Amount);

    private async Task<LedgerEntryEntity> AddLegacyEntryAsync(Guid accountId, LedgerEntryType type, decimal amount)
    {
        var entry = new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = "CLEARING - Store",
            PostedOn = PurchasePostedOn
        };
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Writes a plan the way pre-T3 code left it: schedule present, original entry untouched.</summary>
    private async Task<InstallmentPlanEntity> AddLegacyPlanAsync(Guid accountId, Guid originalLedgerEntryId, int installments)
    {
        var plan = new InstallmentPlanEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TotalAmount = 300m,
            TotalInstallments = installments,
            RemainingInstallments = installments,
            InterestApr = 0m,
            Status = InstallmentPlanStatus.Active,
            Description = "Diferido: legacy",
            OriginalLedgerEntryId = originalLedgerEntryId,
            CreatedOn = PurchasePostedOn
        };

        var principal = Math.Round(300m / installments, 2);
        for (var i = 1; i <= installments; i++)
        {
            plan.AmortizationSchedule.Add(new AmortizationScheduleEntity
            {
                Id = Guid.NewGuid(),
                PlanId = plan.Id,
                InstallmentNumber = i,
                PrincipalAmount = principal,
                InterestAmount = 0m,
                TotalInstallmentAmount = principal,
                DueDate = PurchasePostedOn.AddMonths(i).UtcDateTime,
                Status = InstallmentStatus.Pending,
                CreatedOn = PurchasePostedOn
            });
        }

        _db.InstallmentPlans.Add(plan);
        await _db.SaveChangesAsync();
        return plan;
    }

    private async Task<Guid> SeedAccountAsync()
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
            DefaultInstallmentApr = 0m
        });

        _db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}",
            FullName = "Legacy Plan Customer",
            DocumentId = "1234567890",
            Email = "legacy@test.com",
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

    /// <summary>Minimal in-memory logger so the tests can assert on what the service reports.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
