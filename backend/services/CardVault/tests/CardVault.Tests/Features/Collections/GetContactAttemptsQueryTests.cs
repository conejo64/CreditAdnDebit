using CardVault.Api.Features.Delinquency.Queries;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for GetContactAttemptsQuery handler.
/// RED: written before query class and handler exist.
/// </summary>
public sealed class GetContactAttemptsQueryTests
{
    private static DelinquencyRecordEntity ActiveRecord(Guid? id = null) => new()
    {
        Id            = id ?? Guid.NewGuid(),
        AccountId     = Guid.NewGuid(),
        StatementId   = Guid.NewGuid(),
        OverdueAmount = 200m,
        DaysInArrears = 10,
        Bucket        = DelinquencyBucket.DaysOneToThirty,
        Status        = DelinquencyRecordStatus.Active,
    };

    [Fact]
    public async Task Handle_ReturnsAttempts_SortedByTimestampDescending()
    {
        var dbName = $"T4_Sort_{Guid.NewGuid():N}";
        var record  = ActiveRecord();
        var older   = DateTimeOffset.UtcNow.AddMinutes(-30);
        var newer   = DateTimeOffset.UtcNow.AddMinutes(-5);

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            db.ContactAttempts.AddRange(
                new ContactAttemptEntity { DelinquencyRecordId = record.Id, Channel = ContactChannel.Phone,  Outcome = ContactOutcome.NoAnswer,  AttemptedBy = "a@bank.com", AttemptedOn = older },
                new ContactAttemptEntity { DelinquencyRecordId = record.Id, Channel = ContactChannel.Email,  Outcome = ContactOutcome.Contacted, AttemptedBy = "b@bank.com", AttemptedOn = newer }
            );
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetContactAttemptsQueryHandler(db2);
        var result = await handler.Handle(new GetContactAttemptsQuery(record.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].AttemptedOn.Should().BeAfter(result[1].AttemptedOn);
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoAttemptsExist()
    {
        var dbName = $"T4_Empty_{Guid.NewGuid():N}";
        var record  = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetContactAttemptsQueryHandler(db2);
        var result = await handler.Handle(new GetContactAttemptsQuery(record.Id), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyReturnsAttemptsForSpecifiedRecord()
    {
        var dbName  = $"T4_Filter_{Guid.NewGuid():N}";
        var record1 = ActiveRecord();
        var record2 = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.AddRange(record1, record2);
            db.ContactAttempts.AddRange(
                new ContactAttemptEntity { DelinquencyRecordId = record1.Id, Channel = ContactChannel.Phone, Outcome = ContactOutcome.Contacted, AttemptedBy = "a@bank.com" },
                new ContactAttemptEntity { DelinquencyRecordId = record2.Id, Channel = ContactChannel.SMS,   Outcome = ContactOutcome.NoAnswer,  AttemptedBy = "b@bank.com" }
            );
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetContactAttemptsQueryHandler(db2);
        var result = await handler.Handle(new GetContactAttemptsQuery(record1.Id), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].DelinquencyRecordId.Should().Be(record1.Id);
    }
}
