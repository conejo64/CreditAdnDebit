using CardVault.Api.Background;
using CardVault.Api.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CardVault.Tests.Billing;

/// <summary>
/// SEC-8 / ADR-6 — Characterization tests that pin the exact totals formula for
/// statement generation AND the SwitchTxnConsumer open-statement recalculation path,
/// BEFORE any refactor.
///
/// Scenario (from spec): PreviousBalance=100, Purchases=200, Payments=-50, Fees=10, Interest=5
///   computedBalance = 100 + 200 + (-50) + 10 + 5 = 265
///   InterestDue     = 5
///   FeesDue         = 10
///   PrincipalDue    = max(0, 265 - 5 - 10) = 250
///   TotalPaymentDue = 250 + 5 + 10 = 265
///   NewBalance      = 265
///
/// Task 6.1: Write these tests GREEN against original code as the characterization gate.
/// Task 6.4: Convergence test — RED until Task 6.2 + 6.3 unify both paths via ApplyClosingTotals.
/// </summary>
public sealed class StatementTotalsCharacterizationTests : IDisposable
{
    // ── Expected values from spec scenario ──────────────────────────────────────

    private const decimal ExpectedTotalPaymentDue = 265m;
    private const decimal ExpectedNewBalance = 265m;

    // ── Fixed cycle boundaries ───────────────────────────────────────────────────

    private static readonly DateTime CycleStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CycleEnd = new(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime StatementDate = new(2025, 1, 31);
    private static readonly DateTimeOffset PostedInCycle = new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PostedBeforeCycle = new(2024, 12, 31, 12, 0, 0, TimeSpan.Zero);

    // ── Per-test state ───────────────────────────────────────────────────────────

    private readonly CardVaultDbContext _db;
    private readonly BillingService _billing;
    private readonly MinimumPaymentService _minPay;

    public StatementTotalsCharacterizationTests()
    {
        _db = TestDbContextFactory.Create();
        _minPay = new MinimumPaymentService(_db);
        var policies = new CreditPolicyService(_db);
        var audit = new AuditService(_db);
        _billing = new BillingService(_db, _minPay, policies, audit);
    }

    public void Dispose() => _db.Dispose();

    // ── Task 6.1 — Characterization: BillingService.GenerateStatementAsync ──────

    /// <summary>
    /// Pins: given the prescribed scenario inputs via GenerateStatementAsync,
    /// TotalPaymentDue == 265 and NewBalance == 265.
    /// GREEN against original code; remains GREEN after ApplyClosingTotals extraction.
    /// </summary>
    [Fact(DisplayName = "BillingService.GenerateStatement produces expected totals (characterization)")]
    public async Task BillingService_GenerateStatement_ProducesExpectedTotals()
    {
        // Arrange
        var accountId = await SeedCreditAccountAsync(_db);
        await SeedLedgerEntriesAsync(_db, accountId);

        // Act
        var st = await _billing.GenerateStatementAsync(
            accountId,
            CycleStart, CycleEnd,
            StatementDate,
            dueDateOverride: null,
            CancellationToken.None);

        // Assert — pins current behavior
        st.TotalPaymentDue.Should().Be(ExpectedTotalPaymentDue,
            "TotalPaymentDue = PrincipalDue + InterestDue + FeesDue = 250 + 5 + 10");
        st.NewBalance.Should().Be(ExpectedNewBalance,
            "NewBalance is set equal to TotalPaymentDue by the closing formula");
    }

    // ── Task 6.1 — Characterization: SwitchTxnConsumer UpdateOpenStatementAsync ─

    /// <summary>
    /// Pins: given the same inputs via the consumer open-statement recalculation path,
    /// TotalPaymentDue == 265 and NewBalance == 265.
    ///
    /// UpdateOpenStatementAsync is private static; invoked via reflection to characterize
    /// current behavior without modifying production code before the refactor.
    /// GREEN against original code; remains GREEN after ApplyClosingTotals extraction.
    /// </summary>
    [Fact(DisplayName = "SwitchTxnConsumer.UpdateOpenStatement produces expected totals (characterization)")]
    public async Task SwitchTxnConsumer_UpdateOpenStatement_ProducesExpectedTotals()
    {
        // Arrange — account + ledger entries + an open statement covering the cycle
        var accountId = await SeedCreditAccountAsync(_db);
        await SeedLedgerEntriesAsync(_db, accountId);

        var statementId = Guid.NewGuid();
        _db.Statements.Add(new StatementEntity
        {
            Id = statementId,
            AccountId = accountId,
            CycleStart = CycleStart,
            CycleEnd = CycleEnd,
            StatementDate = StatementDate,
            DueDate = StatementDate.AddDays(15),
            Status = StatementStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(CancellationToken.None);

        // Act — invoke private static UpdateOpenStatementAsync via reflection
        await InvokeUpdateOpenStatementAsync(_db, _minPay, accountId, PostedInCycle, CancellationToken.None);

        // Assert — reload from the same tracked context
        var st = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);

        st.TotalPaymentDue.Should().Be(ExpectedTotalPaymentDue,
            "consumer path must produce same TotalPaymentDue as generate path");
        st.NewBalance.Should().Be(ExpectedNewBalance,
            "consumer path must produce same NewBalance as generate path");
    }

    // ── Task 6.4 — Convergence assertion (RED until 6.2 + 6.3) ─────────────────

    /// <summary>
    /// Drives both paths with identical inputs and asserts TotalPaymentDue and NewBalance
    /// are equal across paths.
    ///
    /// After Task 6.2 + 6.3 unify both paths via ApplyClosingTotals, this test turns GREEN
    /// and acts as the refactor guard. The current characterization tests above remain GREEN
    /// throughout.
    /// </summary>
    [Fact(DisplayName = "Both paths produce identical totals for same inputs (convergence)")]
    public async Task BothPaths_ProduceIdenticalTotals_ForSameInputs()
    {
        // Arrange — two isolated DB instances so paths don't share state
        using var db1 = TestDbContextFactory.Create();
        using var db2 = TestDbContextFactory.Create();

        // Generate path setup
        var acc1 = await SeedCreditAccountAsync(db1);
        await SeedLedgerEntriesAsync(db1, acc1);
        var billing1 = BuildBillingService(db1);

        // Consumer path setup
        var acc2 = await SeedCreditAccountAsync(db2);
        await SeedLedgerEntriesAsync(db2, acc2);

        var statementId2 = Guid.NewGuid();
        db2.Statements.Add(new StatementEntity
        {
            Id = statementId2,
            AccountId = acc2,
            CycleStart = CycleStart,
            CycleEnd = CycleEnd,
            StatementDate = StatementDate,
            DueDate = StatementDate.AddDays(15),
            Status = StatementStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        });
        await db2.SaveChangesAsync(CancellationToken.None);

        // Act — generate path
        var generated = await billing1.GenerateStatementAsync(
            acc1, CycleStart, CycleEnd, StatementDate, dueDateOverride: null, CancellationToken.None);

        // Act — consumer path
        var minPay2 = new MinimumPaymentService(db2);
        await InvokeUpdateOpenStatementAsync(db2, minPay2, acc2, PostedInCycle, CancellationToken.None);

        var recalculated = await db2.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId2);

        // Assert — both paths converge on the same totals
        generated.TotalPaymentDue.Should().Be(recalculated.TotalPaymentDue,
            "after ApplyClosingTotals is shared, both paths must produce identical TotalPaymentDue");
        generated.NewBalance.Should().Be(recalculated.NewBalance,
            "after ApplyClosingTotals is shared, both paths must produce identical NewBalance");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a credit account (with a required customer record) in the given DB context.
    /// Returns the account ID.
    /// </summary>
    private static async Task<Guid> SeedCreditAccountAsync(CardVaultDbContext db)
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}",
            FullName = "Test Customer",
            DocumentId = "1234567890",
            Email = "test@test.com",
            Phone = "+1234567890",
            CreatedOn = DateTimeOffset.UtcNow
        });

        db.Accounts.Add(new CardAccountEntity
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

        await db.SaveChangesAsync(CancellationToken.None);
        return accountId;
    }

    /// <summary>
    /// Seeds the prescribed scenario ledger entries:
    ///   PreviousBalance=100 (pre-cycle purchase), Purchases=200, Payments=-50, Fees=10, Interest=5.
    /// </summary>
    private static async Task SeedLedgerEntriesAsync(CardVaultDbContext db, Guid accountId)
    {
        // Pre-cycle entry that becomes PreviousBalance = 100
        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Purchase,
            Amount = 100m,
            Description = "PRE-CYCLE PURCHASE",
            PostedOn = PostedBeforeCycle
        });

        // Cycle entries
        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Purchase,
            Amount = 200m,
            Description = "CYCLE PURCHASE",
            PostedOn = PostedInCycle
        });

        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Payment,
            Amount = -50m,
            Description = "CYCLE PAYMENT",
            PostedOn = PostedInCycle
        });

        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Fee,
            Amount = 10m,
            Description = "CYCLE FEE",
            PostedOn = PostedInCycle
        });

        db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = LedgerEntryType.Interest,
            Amount = 5m,
            Description = "CYCLE INTEREST",
            PostedOn = PostedInCycle
        });

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Constructs a BillingService with all dependencies pointing to the given DB context.
    /// </summary>
    private static BillingService BuildBillingService(CardVaultDbContext db)
    {
        var minPay = new MinimumPaymentService(db);
        var policies = new CreditPolicyService(db);
        var audit = new AuditService(db);
        return new BillingService(db, minPay, policies, audit);
    }

    /// <summary>
    /// Invokes private static SwitchTxnConsumer.UpdateOpenStatementAsync via reflection.
    /// Signature (post-refactor): (CardVaultDbContext db, MinimumPaymentService minPay,
    ///             BillingService billing, Guid accountId, DateTimeOffset postedOn, CancellationToken ct)
    /// </summary>
    private static async Task InvokeUpdateOpenStatementAsync(
        CardVaultDbContext db,
        MinimumPaymentService minPay,
        Guid accountId,
        DateTimeOffset postedOn,
        CancellationToken ct)
    {
        var billingService = BuildBillingService(db);

        var method = typeof(SwitchTxnConsumer).GetMethod(
            "UpdateOpenStatementAsync",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(CardVaultDbContext), typeof(MinimumPaymentService), typeof(BillingService), typeof(Guid), typeof(DateTimeOffset), typeof(CancellationToken)],
            modifiers: null);

        method.Should().NotBeNull(
            "SwitchTxnConsumer must have a private static UpdateOpenStatementAsync(db, minPay, billing, accountId, postedOn, ct) method");

        var task = (Task)method!.Invoke(null, [db, minPay, billingService, accountId, postedOn, ct])!;
        await task;
    }
}
