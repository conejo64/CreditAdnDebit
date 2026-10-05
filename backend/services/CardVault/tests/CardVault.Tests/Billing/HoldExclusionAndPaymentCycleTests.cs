using System.Reflection;
using CardVault.Application.Contracts;
using CardVault.Application.Features.Billing.Commands;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Messaging.Consumers;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Billing;

/// <summary>
/// Gate 0 / T4. Three money-path rules:
/// (a) authorization holds are shadow items: an open hold contributes nothing to the daily interest
///     base or to a statement's previous balance, and a captured hold counts exactly once, through
///     its Clearing entry;
/// (b) a payment posted after the cut-off belongs to the next cycle: it is listed on the next
///     statement and reduces that statement's balance, not the closed one's lines;
/// (c) statement totals and the minimum payment are recomputed after the payment has been
///     allocated to its buckets, so the response and the persisted statement agree with what is
///     still owed.
/// </summary>
public sealed class HoldExclusionAndPaymentCycleTests : IDisposable
{
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Jan =
        (new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 1, 31));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Feb =
        (new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 2, 28, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 2, 28));
    private static readonly (DateTime Start, DateTime End, DateTime StatementDate) Mar =
        (new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(2025, 3, 31));

    private readonly CardVaultDbContext _db;
    private readonly BillingService _billing;
    private readonly MinimumPaymentService _minPay;
    private readonly DailyInterestAccrualService _accrual;
    private readonly PaymentAllocatorService _allocator;

    public HoldExclusionAndPaymentCycleTests()
    {
        _db = TestDbContextFactory.Create();
        var audit = new AuditService(_db);
        var policies = new CreditPolicyService(_db);
        _minPay = new MinimumPaymentService(_db);
        _billing = new BillingService(_db, _minPay, policies, audit);
        _accrual = new DailyInterestAccrualService(_db, policies, audit);
        _allocator = new PaymentAllocatorService(_db);
    }

    public void Dispose() => _db.Dispose();

    // ── (a) Holds are excluded from the interest base ───────────────────────────

    [Fact(DisplayName = "An open authorization hold contributes 0 to the end-of-day interest base")]
    public async Task Open_hold_is_not_part_of_the_interest_base()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 100m, On(2025, 1, 10));
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 50m, On(2025, 1, 12));
        await _db.SaveChangesAsync();

        await _accrual.AccrueAsync(accountId, new DateOnly(2025, 1, 15), new DateOnly(2025, 1, 15), CancellationToken.None);

        var record = await _db.InterestAccrualRecords.AsNoTracking()
            .SingleAsync(r => r.AccountId == accountId && r.AccrualDate == new DateOnly(2025, 1, 15));
        record.BalanceBase.Should().Be(100m, "a pending authorization is not a posted debt");
    }

    [Fact(DisplayName = "A captured hold (hold + Clearing) is part of the interest base exactly once, through the Clearing")]
    public async Task Captured_hold_counts_once_in_the_interest_base()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 100m, On(2025, 1, 10));
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 50m, On(2025, 1, 12));
        AddEntry(accountId, LedgerEntryType.Clearing, 50m, On(2025, 1, 13));
        await _db.SaveChangesAsync();

        await _accrual.AccrueAsync(accountId, new DateOnly(2025, 1, 15), new DateOnly(2025, 1, 15), CancellationToken.None);

        var record = await _db.InterestAccrualRecords.AsNoTracking()
            .SingleAsync(r => r.AccountId == accountId && r.AccrualDate == new DateOnly(2025, 1, 15));
        record.BalanceBase.Should().Be(150m, "100 purchase + 50 clearing; the hold itself must not be added on top");
    }

    [Fact(DisplayName = "An account whose only activity is an open hold accrues no interest")]
    public async Task Hold_only_account_accrues_nothing()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 50m, On(2025, 1, 2));
        await _db.SaveChangesAsync();

        await _accrual.AccrueAsync(accountId, new DateOnly(2025, 1, 15), new DateOnly(2025, 1, 15), CancellationToken.None);

        (await _db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId && x.Type == LedgerEntryType.Interest).SumAsync(x => x.Amount))
            .Should().Be(0m);
    }

    // ── (a) Holds are excluded from statement balances ──────────────────────────

    [Fact(DisplayName = "Statement previous balance and purchases exclude open holds; a captured hold counts once via Clearing")]
    public async Task Statement_bases_exclude_holds()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 100m, On(2024, 12, 20));           // previous balance
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 50m, On(2024, 12, 28));   // open hold before the cycle
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 30m, On(2025, 1, 10));    // open hold in the cycle
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 40m, On(2025, 1, 11));    // captured hold ...
        AddEntry(accountId, LedgerEntryType.Clearing, 40m, On(2025, 1, 12));             // ... and its clearing
        await _db.SaveChangesAsync();

        var st = await GenerateAsync(accountId, Jan);

        st.PreviousBalance.Should().Be(100m, "the pre-cycle open hold is not posted debt");
        st.Purchases.Should().Be(40m, "only the clearing is a purchase; the hold behind it is not added again");
        st.NewBalance.Should().Be(140m);

        var lines = await _billing.GetLinesAsync(st.Id, CancellationToken.None);
        lines.Should().NotContain(l => l.Type == LedgerEntryType.AuthorizationHold, "pending authorizations are not statement lines");

        var holds = await _db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId && x.Type == LedgerEntryType.AuthorizationHold).ToListAsync();
        holds.Should().OnlyContain(h => h.StatementId == null, "shadow entries are never invoiced");
    }

    [Fact(DisplayName = "Consumer open-statement recalculation also excludes a pre-cycle open hold from the previous balance")]
    public async Task Consumer_recalculation_excludes_holds_from_previous_balance()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 100m, On(2024, 12, 20));
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 50m, On(2024, 12, 28));
        AddEntry(accountId, LedgerEntryType.AuthorizationHold, 30m, On(2025, 1, 10));
        AddEntry(accountId, LedgerEntryType.Purchase, 20m, On(2025, 1, 15));
        await _db.SaveChangesAsync();

        var statementId = Guid.NewGuid();
        _db.Statements.Add(new StatementEntity
        {
            Id = statementId,
            AccountId = accountId,
            CycleStart = Jan.Start,
            CycleEnd = Jan.End,
            StatementDate = Jan.StatementDate,
            DueDate = Jan.StatementDate.AddDays(15),
            Status = StatementStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        await InvokeUpdateOpenStatementAsync(accountId, On(2025, 1, 15));

        var st = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == statementId);
        st.PreviousBalance.Should().Be(100m);
        st.Purchases.Should().Be(20m);
        st.NewBalance.Should().Be(120m);
    }

    // ── (b) Payments land in the next cycle ─────────────────────────────────────

    [Fact(DisplayName = "A payment posted after the cut-off is listed on the next statement and reduces its balance")]
    public async Task Payment_after_cutoff_lands_in_the_next_statement()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 200m, On(2025, 1, 15));
        await _db.SaveChangesAsync();

        var jan = await GenerateAsync(accountId, Jan);
        jan.NewBalance.Should().Be(200m);

        await _billing.ApplyStatementPaymentAsync(jan.Id, 50m, On(2025, 2, 5), CancellationToken.None);

        var feb = await GenerateAsync(accountId, Feb);

        feb.PreviousBalance.Should().Be(200m);
        feb.Payments.Should().Be(-50m, "the payment belongs to the cycle it was posted in");
        feb.NewBalance.Should().Be(150m);

        var febLines = await _billing.GetLinesAsync(feb.Id, CancellationToken.None);
        febLines.Should().ContainSingle(l => l.Type == LedgerEntryType.Payment && l.Amount == -50m);

        var janLines = await _billing.GetLinesAsync(jan.Id, CancellationToken.None);
        janLines.Should().NotContain(l => l.Type == LedgerEntryType.Payment, "the closed statement does not change after the cut-off");

        var payment = await _db.LedgerEntries.AsNoTracking().SingleAsync(x => x.AccountId == accountId && x.Type == LedgerEntryType.Payment);
        payment.StatementId.Should().Be(feb.Id);

        var mar = await GenerateAsync(accountId, Mar);
        mar.PreviousBalance.Should().Be(150m, "the payment is carried into every later cycle");
    }

    // ── (c) Totals are recomputed after allocation ──────────────────────────────

    [Fact(DisplayName = "Applying a payment recomputes totals and minimum payment after the allocation to buckets")]
    public async Task Payment_totals_reflect_post_allocation_buckets()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 100m, On(2024, 12, 20));
        AddEntry(accountId, LedgerEntryType.Purchase, 200m, On(2025, 1, 15));
        AddEntry(accountId, LedgerEntryType.Payment, -50m, On(2025, 1, 15));
        AddEntry(accountId, LedgerEntryType.Fee, 10m, On(2025, 1, 15));
        AddEntry(accountId, LedgerEntryType.Interest, 5m, On(2025, 1, 15));
        await _db.SaveChangesAsync();

        var st = await GenerateAsync(accountId, Jan);
        st.TotalPaymentDue.Should().Be(265m);
        st.MinimumPayment.Should().Be(27.50m, "5 interest + 10 fees + max(10, 5 % of 250)");

        var handler = new ApplyPaymentCommandHandler(_billing, _allocator);
        var result = await handler.Handle(new ApplyPaymentCommand(st.Id, new ApplyPaymentRequest(100m, On(2025, 2, 5))), CancellationToken.None);

        var persisted = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == st.Id);
        persisted.PaidAmount.Should().Be(100m);
        persisted.InterestDue.Should().Be(0m);
        persisted.FeesDue.Should().Be(0m);
        persisted.PrincipalDue.Should().Be(165m, "100 pays 5 interest, 10 fees and 85 principal");
        persisted.TotalPaymentDue.Should().Be(165m, "totals must follow the allocated buckets");
        persisted.NewBalance.Should().Be(165m);
        persisted.MinimumPayment.Should().Be(10m, "max(10, 5 % of 165) with nothing left in interest or fees");

        var returned = ExtractStatement(result);
        returned.TotalPaymentDue.Should().Be(165m, "the response must not expose pre-allocation totals");
        returned.MinimumPayment.Should().Be(10m);
    }

    [Fact(DisplayName = "A full payment leaves zero totals and a zero minimum payment")]
    public async Task Full_payment_zeroes_totals_and_minimum()
    {
        var accountId = await SeedCreditAccountAsync();
        AddEntry(accountId, LedgerEntryType.Purchase, 200m, On(2025, 1, 15));
        AddEntry(accountId, LedgerEntryType.Fee, 10m, On(2025, 1, 15));
        AddEntry(accountId, LedgerEntryType.Interest, 5m, On(2025, 1, 15));
        await _db.SaveChangesAsync();

        var st = await GenerateAsync(accountId, Jan);
        st.TotalPaymentDue.Should().Be(215m);

        var handler = new ApplyPaymentCommandHandler(_billing, _allocator);
        await handler.Handle(new ApplyPaymentCommand(st.Id, new ApplyPaymentRequest(215m, On(2025, 2, 5))), CancellationToken.None);

        var persisted = await _db.Statements.AsNoTracking().FirstAsync(x => x.Id == st.Id);
        persisted.TotalPaymentDue.Should().Be(0m);
        persisted.NewBalance.Should().Be(0m);
        persisted.MinimumPayment.Should().Be(0m, "nothing is owed on a fully paid statement");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static DateTimeOffset On(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private Task<StatementEntity> GenerateAsync(Guid accountId, (DateTime Start, DateTime End, DateTime StatementDate) cycle) =>
        _billing.GenerateStatementAsync(accountId, cycle.Start, cycle.End, cycle.StatementDate, dueDateOverride: null, CancellationToken.None);

    private void AddEntry(Guid accountId, LedgerEntryType type, decimal amount, DateTimeOffset postedOn) =>
        _db.LedgerEntries.Add(new LedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Description = type.ToString().ToUpperInvariant(),
            PostedOn = postedOn
        });

    private static StatementEntity ExtractStatement(Microsoft.AspNetCore.Http.IResult result)
    {
        var value = result.GetType().GetProperty("Value")!.GetValue(result)!;
        var statement = value.GetType().GetProperty("statement")!.GetValue(value);
        return statement.Should().BeOfType<StatementEntity>().Subject;
    }

    private async Task InvokeUpdateOpenStatementAsync(Guid accountId, DateTimeOffset postedOn)
    {
        var method = typeof(SwitchTxnConsumer).GetMethod(
            "UpdateOpenStatementAsync",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(CardVaultDbContext), typeof(MinimumPaymentService), typeof(BillingService), typeof(Guid), typeof(DateTimeOffset), typeof(CancellationToken)],
            modifiers: null);

        method.Should().NotBeNull();
        await (Task)method!.Invoke(null, [_db, _minPay, _billing, accountId, postedOn, CancellationToken.None])!;
    }

    private async Task<Guid> SeedCreditAccountAsync()
    {
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _db.Customers.Add(new CustomerEntity
        {
            Id = customerId,
            CustomerNumber = $"C{customerId:N}",
            FullName = "Hold Exclusion Test Customer",
            DocumentId = "1234567890",
            Email = "holds@test.com",
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
