using CardVault.Application.Features.Delinquency.Commands;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Collections;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD tests for RegisterContactAttemptCommand handler.
/// RED: written before the command class and handler exist.
/// </summary>
public sealed class RegisterContactAttemptCommandTests
{
    private static DelinquencyRecordEntity ActiveRecord(Guid? id = null) => new()
    {
        Id            = id ?? Guid.NewGuid(),
        AccountId     = Guid.NewGuid(),
        StatementId   = Guid.NewGuid(),
        OverdueAmount = 500m,
        DaysInArrears = 15,
        Bucket        = DelinquencyBucket.DaysOneToThirty,
        Status        = DelinquencyRecordStatus.Active,
    };

    // ─────────────────────────────────────────────
    // Happy path
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_PersistsContactAttempt_WhenRecordIsActive()
    {
        var dbName = $"T2_Happy_{Guid.NewGuid():N}";
        var record = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using (var db = TestDbContextFactory.CreateSecondContext(dbName))
        {
            var handler = new RegisterContactAttemptCommandHandler(db);
            var cmd = new RegisterContactAttemptCommand(
                DelinquencyRecordId: record.Id,
                Channel: ContactChannel.Phone,
                Outcome: ContactOutcome.Contacted,
                Notes: "Customer promised payment by Friday.",
                AttemptedBy: "agent@bank.com"
            );

            var result = await handler.Handle(cmd, CancellationToken.None);
            result.Should().NotBeEmpty();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var attempt = db2.ContactAttempts.Single();
        attempt.DelinquencyRecordId.Should().Be(record.Id);
        attempt.Channel.Should().Be(ContactChannel.Phone);
        attempt.Outcome.Should().Be(ContactOutcome.Contacted);
        attempt.Notes.Should().Be("Customer promised payment by Friday.");
        attempt.AttemptedBy.Should().Be("agent@bank.com");
        attempt.AttemptedOn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_AcceptsNullNotes()
    {
        var dbName = $"T2_NullNotes_{Guid.NewGuid():N}";
        var record = ActiveRecord();

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new RegisterContactAttemptCommandHandler(db2);
        var result = await handler.Handle(
            new RegisterContactAttemptCommand(record.Id, ContactChannel.SMS, ContactOutcome.NoAnswer, null, "agent@bank.com"),
            CancellationToken.None);
        result.Should().NotBeEmpty();
    }

    // ─────────────────────────────────────────────
    // Validation: record not found
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_Throws_WhenDelinquencyRecordNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new RegisterContactAttemptCommandHandler(db);
        var cmd = new RegisterContactAttemptCommand(
            Guid.NewGuid(), ContactChannel.Phone, ContactOutcome.Contacted, null, "agent@bank.com");

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
        var dbName = $"T2_Resolved_{Guid.NewGuid():N}";
        var record = ActiveRecord();
        record.Status = DelinquencyRecordStatus.Resolved;

        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyRecords.Add(record);
            await db.SaveChangesAsync();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        var handler = new RegisterContactAttemptCommandHandler(db2);
        var cmd = new RegisterContactAttemptCommand(record.Id, ContactChannel.Email, ContactOutcome.Contacted, null, "agent@bank.com");

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolved*");
    }
}
