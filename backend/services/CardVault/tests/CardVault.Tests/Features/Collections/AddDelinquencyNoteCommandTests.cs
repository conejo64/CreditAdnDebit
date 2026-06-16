using CardVault.Api.Features.Delinquency.Commands;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for AddDelinquencyNoteCommand handler.
/// RED: written before the command class and handler exist.
/// </summary>
public sealed class AddDelinquencyNoteCommandTests
{
    private static DelinquencyRecordEntity ActiveRecord(Guid? id = null) => new()
    {
        Id            = id ?? Guid.NewGuid(),
        AccountId     = Guid.NewGuid(),
        StatementId   = Guid.NewGuid(),
        OverdueAmount = 300m,
        DaysInArrears = 5,
        Bucket        = DelinquencyBucket.DaysOneToThirty,
        Status        = DelinquencyRecordStatus.Active,
    };

    // ─────────────────────────────────────────────
    // Happy path
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_PersistsNote_WhenRecordIsActive()
    {
        var dbName = $"T3_Happy_{Guid.NewGuid():N}";
        var record = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using (var db = TestDbContextFactory.CreateSecondContext(dbName))
        {
            var handler = new AddDelinquencyNoteCommandHandler(db);
            var cmd = new AddDelinquencyNoteCommand(
                DelinquencyRecordId: record.Id,
                Content: "Escalated to supervisor for manual review.",
                CreatedBy: "supervisor@bank.com"
            );

            var result = await handler.Handle(cmd, CancellationToken.None);
            result.Should().NotBeEmpty();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var note = db2.DelinquencyNotes.Single();
        note.DelinquencyRecordId.Should().Be(record.Id);
        note.Content.Should().Be("Escalated to supervisor for manual review.");
        note.CreatedBy.Should().Be("supervisor@bank.com");
        note.CreatedOn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ─────────────────────────────────────────────
    // Validation: record not found
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_Throws_WhenDelinquencyRecordNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new AddDelinquencyNoteCommandHandler(db);
        var cmd = new AddDelinquencyNoteCommand(Guid.NewGuid(), "Some note.", "agent@bank.com");

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─────────────────────────────────────────────
    // Validation: resolved record is immutable
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_Throws_WhenDelinquencyRecordIsResolved()
    {
        var dbName = $"T3_Resolved_{Guid.NewGuid():N}";
        var record = ActiveRecord();
        record.Status = DelinquencyRecordStatus.Resolved;

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new AddDelinquencyNoteCommandHandler(db2);
        var cmd = new AddDelinquencyNoteCommand(record.Id, "Some note.", "agent@bank.com");

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolved*");
    }
}
