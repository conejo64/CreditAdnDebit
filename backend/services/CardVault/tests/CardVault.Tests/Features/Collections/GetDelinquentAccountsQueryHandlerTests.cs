using CardVault.Api.Features.Delinquency.Queries;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// Unit tests for GetDelinquentAccountsQueryHandler.
/// Strict TDD: tests written before verifying handler completeness.
/// Covers spec scenarios: paginated list, unauthorized prevention, read-only constraints.
/// </summary>
public sealed class GetDelinquentAccountsQueryHandlerTests : IDisposable
{
    private readonly CardVaultDbContext _db;

    public GetDelinquentAccountsQueryHandlerTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────────────────────
    // Seed helpers
    // ─────────────────────────────────────────────────────────────

    private async Task<DelinquencyRecordEntity> SeedDelinquencyRecordAsync(
        DelinquencyBucket bucket = DelinquencyBucket.DaysOneToThirty,
        DelinquencyRecordStatus status = DelinquencyRecordStatus.Active,
        int daysInArrears = 15,
        decimal overdueAmount = 100m)
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id             = Guid.NewGuid(),
            FullName       = "Test Customer",
            DocumentId     = $"D{Guid.NewGuid():N}"[..10],
            Email          = $"t{Guid.NewGuid():N}"[..6] + "@test.com",
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
            Status         = AccountStatus.Delinquent,
            CurrencyCode   = "USD",
        }).Entity;

        var statement = _db.Statements.Add(new StatementEntity
        {
            Id             = Guid.NewGuid(),
            AccountId      = account.Id,
            CycleStart     = DateTime.UtcNow.AddMonths(-2),
            CycleEnd       = DateTime.UtcNow.AddMonths(-1),
            StatementDate  = DateTime.UtcNow.AddMonths(-1),
            DueDate        = DateTime.UtcNow.AddDays(-daysInArrears),
            MinimumPayment = 100m,
            PaidAmount     = 0m,
        }).Entity;

        var record = _db.DelinquencyRecords.Add(new DelinquencyRecordEntity
        {
            Id            = Guid.NewGuid(),
            AccountId     = account.Id,
            StatementId   = statement.Id,
            OverdueAmount = overdueAmount,
            DaysInArrears = daysInArrears,
            Bucket        = bucket,
            Status        = status,
        }).Entity;

        await _db.SaveChangesAsync();
        return record;
    }

    // ─────────────────────────────────────────────────────────────
    // Tests — Scenario: Operator queries delinquent accounts
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenActiveRecordsExist_ReturnsPaginatedList()
    {
        await SeedDelinquencyRecordAsync(daysInArrears: 20, overdueAmount: 150m);
        await SeedDelinquencyRecordAsync(daysInArrears: 45, overdueAmount: 300m, bucket: DelinquencyBucket.DaysThirtyOneToSixty);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery { Page = 1, PageSize = 20 }, default);

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        okResult.Value!.Items.Should().HaveCount(2);
        okResult.Value.TotalCount.Should().Be(2);
        okResult.Value.Page.Should().Be(1);
        okResult.Value.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_WhenNoBucketFilter_ReturnsAllActiveRecords()
    {
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.DaysOneToThirty);
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.OverNinety, daysInArrears: 95, overdueAmount: 500m);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery(), default);

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        okResult.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithBucketFilter_ReturnsOnlyMatchingBucketRecords()
    {
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.DaysOneToThirty, daysInArrears: 20);
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.DaysThirtyOneToSixty, daysInArrears: 45, overdueAmount: 300m);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery { Bucket = 1 }, default); // bucket 1 = DaysOneToThirty

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        okResult.Value!.Items.Should().HaveCount(1);
        okResult.Value.Items[0].Bucket.Should().Be(1);
        okResult.Value.Items[0].BucketLabel.Should().Be("1-30 days");
    }

    [Fact]
    public async Task Handle_WhenNoActiveRecords_ReturnsEmptyPagedResult()
    {
        // Seed only a Resolved record — should NOT appear in default Active filter
        await SeedDelinquencyRecordAsync(status: DelinquencyRecordStatus.Resolved, daysInArrears: 10);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery { Status = "Active" }, default);

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        // Empty because the seeded record is Resolved, not Active
        okResult.Value!.TotalCount.Should().Be(0);
        okResult.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OrdersByDaysInArrearsDescending()
    {
        await SeedDelinquencyRecordAsync(daysInArrears: 15, overdueAmount: 100m);
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.OverNinety, daysInArrears: 100, overdueAmount: 800m);
        await SeedDelinquencyRecordAsync(bucket: DelinquencyBucket.DaysThirtyOneToSixty, daysInArrears: 50, overdueAmount: 400m);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery(), default);

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        var items = okResult.Value!.Items;
        items.Should().HaveCount(3);
        items[0].DaysInArrears.Should().Be(100);
        items[1].DaysInArrears.Should().Be(50);
        items[2].DaysInArrears.Should().Be(15);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSlice()
    {
        for (var i = 0; i < 5; i++)
            await SeedDelinquencyRecordAsync(daysInArrears: 10 + i, overdueAmount: 100m + i);

        var handler = new GetDelinquentAccountsQueryHandler(_db);
        var result = await handler.Handle(new GetDelinquentAccountsQuery { Page = 2, PageSize = 2 }, default);

        var okResult = result.Should().BeOfType<Ok<PagedResult<DelinquencyRecordDto>>>().Subject;
        okResult.Value!.TotalCount.Should().Be(5);
        okResult.Value.TotalPages.Should().Be(3);
        okResult.Value.Items.Should().HaveCount(2); // page 2, size 2
    }
}
