using CardVault.Api.Features.Delinquency.Queries;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for GetDelinquencyNotesQuery handler.
/// RED: written before query class and handler exist.
/// </summary>
public sealed class GetDelinquencyNotesQueryTests
{
    private static DelinquencyRecordEntity ActiveRecord(Guid? id = null) => new()
    {
        Id            = id ?? Guid.NewGuid(),
        AccountId     = Guid.NewGuid(),
        StatementId   = Guid.NewGuid(),
        OverdueAmount = 150m,
        DaysInArrears = 8,
        Bucket        = DelinquencyBucket.DaysOneToThirty,
        Status        = DelinquencyRecordStatus.Active,
    };

    [Fact]
    public async Task Handle_ReturnsNotes_SortedByCreatedOnDescending()
    {
        var dbName = $"T5_Sort_{Guid.NewGuid():N}";
        var record  = ActiveRecord();
        var older   = DateTimeOffset.UtcNow.AddHours(-2);
        var newer   = DateTimeOffset.UtcNow.AddMinutes(-10);

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            db.DelinquencyNotes.AddRange(
                new DelinquencyNoteEntity { DelinquencyRecordId = record.Id, Content = "First note.",  CreatedBy = "a@bank.com", CreatedOn = older },
                new DelinquencyNoteEntity { DelinquencyRecordId = record.Id, Content = "Second note.", CreatedBy = "b@bank.com", CreatedOn = newer }
            );
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetDelinquencyNotesQueryHandler(db2);
        var result = await handler.Handle(new GetDelinquencyNotesQuery(record.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].CreatedOn.Should().BeAfter(result[1].CreatedOn);
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoNotesExist()
    {
        var dbName = $"T5_Empty_{Guid.NewGuid():N}";
        var record  = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetDelinquencyNotesQueryHandler(db2);
        var result = await handler.Handle(new GetDelinquencyNotesQuery(record.Id), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyReturnsNotesForSpecifiedRecord()
    {
        var dbName  = $"T5_Filter_{Guid.NewGuid():N}";
        var record1 = ActiveRecord();
        var record2 = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.AddRange(record1, record2);
            db.DelinquencyNotes.AddRange(
                new DelinquencyNoteEntity { DelinquencyRecordId = record1.Id, Content = "Note for R1.", CreatedBy = "a@bank.com" },
                new DelinquencyNoteEntity { DelinquencyRecordId = record2.Id, Content = "Note for R2.", CreatedBy = "b@bank.com" }
            );
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new GetDelinquencyNotesQueryHandler(db2);
        var result = await handler.Handle(new GetDelinquencyNotesQuery(record1.Id), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].DelinquencyRecordId.Should().Be(record1.Id);
    }
}
