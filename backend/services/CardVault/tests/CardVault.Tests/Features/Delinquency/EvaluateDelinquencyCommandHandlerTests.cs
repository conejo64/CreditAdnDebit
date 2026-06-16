using CardVault.Api.Features.Delinquency.Commands;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Features.Delinquency;

/// <summary>
/// Unit tests for EvaluateDelinquencyCommandHandler.
/// Strict TDD: tests were written against the spec scenarios BEFORE the handler.
/// Each test corresponds to a named scenario in the spec.
/// </summary>
public sealed class EvaluateDelinquencyCommandHandlerTests : IDisposable
{
    private readonly CardVaultDbContext _db;

    public EvaluateDelinquencyCommandHandlerTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<(CardAccountEntity Account, StatementEntity Statement)> SeedDelinquentScenarioAsync(
        decimal minimumPayment,
        decimal paidAmount,
        DateTime dueDate)
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id             = Guid.NewGuid(),
            FullName       = "Delinquency Test Customer",
            DocumentId     = $"D{Guid.NewGuid():N}"[..10],
            Email          = "dlq@test.com",
            Phone          = "+5939990001",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id             = Guid.NewGuid(),
            CustomerId     = customer.Id,
            AccountType    = AccountType.Credit,
            ProductCode    = "VISA_CLASSIC",
            CreditLimit    = 5000m,
            AvailableLimit = 5000m,
            AccountNumber  = $"A{Guid.NewGuid():N}"[..10],
            Status         = AccountStatus.Active,
            CurrencyCode   = "USD",
        }).Entity;

        var statement = _db.Statements.Add(new StatementEntity
        {
            Id             = Guid.NewGuid(),
            AccountId      = account.Id,
            StatementDate  = dueDate.AddDays(-20),
            DueDate        = dueDate,
            CycleStart     = dueDate.AddDays(-50),
            CycleEnd       = dueDate.AddDays(-20),
            MinimumPayment = minimumPayment,
            PaidAmount     = paidAmount,
            NewBalance     = 1000m,
            StatementBalance = 1000m,
        }).Entity;

        await _db.SaveChangesAsync();
        return (account, statement);
    }

    // ─────────────────────────────────────────────────────────────
    // Scenario 1: Unpaid minimum payment → account becomes DELINQUENT
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateDelinquency_ShouldMarkAccountDelinquent_WhenMinimumPaymentNotMet()
    {
        var dueDate = DateTime.UtcNow.Date.AddDays(-1); // Yesterday
        var (account, _) = await SeedDelinquentScenarioAsync(
            minimumPayment: 50m,
            paidAmount:     0m,
            dueDate:        dueDate);

        var handler = new EvaluateDelinquencyCommandHandler(_db);
        await handler.Handle(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), CancellationToken.None);

        var updated = await _db.Accounts.FindAsync(account.Id);
        updated!.Status.Should().Be(AccountStatus.Delinquent);
    }

    // ─────────────────────────────────────────────────────────────
    // Scenario 2: Delinquency record with correct bucket
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateDelinquency_ShouldCreateDelinquencyRecord_WithCorrectBucket()
    {
        var dueDate = DateTime.UtcNow.Date.AddDays(-1);
        var (account, statement) = await SeedDelinquentScenarioAsync(
            minimumPayment: 50m,
            paidAmount:     0m,
            dueDate:        dueDate);

        var handler = new EvaluateDelinquencyCommandHandler(_db);
        await handler.Handle(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), CancellationToken.None);

        var record = await _db.DelinquencyRecords
            .FirstOrDefaultAsync(x => x.AccountId == account.Id);

        record.Should().NotBeNull();
        record!.StatementId.Should().Be(statement.Id);
        record.OverdueAmount.Should().Be(50m);
        record.DaysInArrears.Should().Be(1);
        record.Bucket.Should().Be(DelinquencyBucket.DaysOneToThirty);
        record.Status.Should().Be(DelinquencyRecordStatus.Active);
    }

    // ─────────────────────────────────────────────────────────────
    // Scenario 3: Fully paid minimum → account stays ACTIVE, no record
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateDelinquency_ShouldNotAffectAccounts_WhenMinimumPaymentIsMet()
    {
        var dueDate = DateTime.UtcNow.Date.AddDays(-1);
        var (account, _) = await SeedDelinquentScenarioAsync(
            minimumPayment: 50m,
            paidAmount:     50m, // exactly covered
            dueDate:        dueDate);

        var handler = new EvaluateDelinquencyCommandHandler(_db);
        await handler.Handle(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), CancellationToken.None);

        var updated = await _db.Accounts.FindAsync(account.Id);
        updated!.Status.Should().Be(AccountStatus.Active);

        var records = await _db.DelinquencyRecords
            .Where(x => x.AccountId == account.Id).ToListAsync();
        records.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────
    // Scenario 4: Aging across a bucket boundary
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateDelinquency_ShouldIncrementDaysAndBucket_WhenAging()
    {
        // Seed as already-delinquent with an active record at day 30
        var dueDate  = DateTime.UtcNow.Date.AddDays(-31);
        var (account, statement) = await SeedDelinquentScenarioAsync(
            minimumPayment: 50m,
            paidAmount:     0m,
            dueDate:        dueDate);

        // Put account in delinquent state with existing record at 30 days
        account.Status = AccountStatus.Delinquent;
        _db.DelinquencyRecords.Add(new DelinquencyRecordEntity
        {
            Id             = Guid.NewGuid(),
            AccountId      = account.Id,
            StatementId    = statement.Id,
            OverdueAmount  = 50m,
            DaysInArrears  = 30,
            Bucket         = DelinquencyBucket.DaysOneToThirty,
            Status         = DelinquencyRecordStatus.Active,
        });
        await _db.SaveChangesAsync();

        // Evaluate 1 day later: now 31 days in arrears
        var handler = new EvaluateDelinquencyCommandHandler(_db);
        await handler.Handle(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), CancellationToken.None);

        var record = await _db.DelinquencyRecords
            .FirstAsync(x => x.AccountId == account.Id && x.Status == DelinquencyRecordStatus.Active);

        record.DaysInArrears.Should().Be(31);
        record.Bucket.Should().Be(DelinquencyBucket.DaysThirtyOneToSixty);
    }

    // ─────────────────────────────────────────────────────────────
    // Scenario 5: Delinquency resolved when overdue paid
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateDelinquency_ShouldResolveDelinquency_WhenOverduePaid()
    {
        var dueDate = DateTime.UtcNow.Date.AddDays(-5);
        var (account, statement) = await SeedDelinquentScenarioAsync(
            minimumPayment: 50m,
            paidAmount:     50m, // now fully paid
            dueDate:        dueDate);

        // Simulate that account was already delinquent (previous evaluation had marked it)
        account.Status = AccountStatus.Delinquent;
        _db.DelinquencyRecords.Add(new DelinquencyRecordEntity
        {
            Id            = Guid.NewGuid(),
            AccountId     = account.Id,
            StatementId   = statement.Id,
            OverdueAmount = 50m,
            DaysInArrears = 5,
            Bucket        = DelinquencyBucket.DaysOneToThirty,
            Status        = DelinquencyRecordStatus.Active,
        });
        await _db.SaveChangesAsync();

        var handler = new EvaluateDelinquencyCommandHandler(_db);
        await handler.Handle(new EvaluateDelinquencyCommand(DateTime.UtcNow.Date), CancellationToken.None);

        var updatedAccount = await _db.Accounts.FindAsync(account.Id);
        updatedAccount!.Status.Should().Be(AccountStatus.Active);

        var record = await _db.DelinquencyRecords
            .FirstAsync(x => x.AccountId == account.Id);
        record.Status.Should().Be(DelinquencyRecordStatus.Resolved);
        record.ResolvedOn.Should().NotBeNull();
    }
}
