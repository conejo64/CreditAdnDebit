using CardVault.Api.Features.Delinquency.Queries;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for the GetDelinquentAccountsQuery contract and PagedResult.
/// RED: written before the types exist.
/// </summary>
public sealed class GetDelinquentAccountsQueryContractTests
{
    [Fact]
    public void GetDelinquentAccountsQuery_DefaultValues_AreReasonable()
    {
        var query = new GetDelinquentAccountsQuery();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(20);
        query.Bucket.Should().BeNull();
        query.Status.Should().Be("Active");
    }

    [Fact]
    public void GetDelinquentAccountsQuery_CanSetAllProperties()
    {
        var query = new GetDelinquentAccountsQuery
        {
            Page = 3,
            PageSize = 50,
            Bucket = 2,
            Status = "Resolved"
        };

        query.Page.Should().Be(3);
        query.PageSize.Should().Be(50);
        query.Bucket.Should().Be(2);
        query.Status.Should().Be("Resolved");
    }

    [Fact]
    public void PagedResult_WrapsItemsAndPaginationMetadata()
    {
        var items = new List<DelinquencyRecordDto>
        {
            new DelinquencyRecordDto
            {
                Id = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                StatementId = Guid.NewGuid(),
                OverdueAmount = 150.00m,
                DaysInArrears = 35,
                Bucket = 2,
                BucketLabel = "31-60 days",
                Status = "Active",
                CreatedOn = DateTimeOffset.UtcNow,
                UpdatedOn = DateTimeOffset.UtcNow,
            }
        };

        var result = new PagedResult<DelinquencyRecordDto>(items, totalCount: 1, page: 1, pageSize: 20);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void PagedResult_TotalPages_RoundsUp()
    {
        var items = Enumerable.Range(1, 5).Select(_ => new DelinquencyRecordDto()).ToList();
        var result = new PagedResult<DelinquencyRecordDto>(items, totalCount: 23, page: 1, pageSize: 5);

        // 23 items / 5 per page = 4.6 → ceiling = 5
        result.TotalPages.Should().Be(5);
    }
}
