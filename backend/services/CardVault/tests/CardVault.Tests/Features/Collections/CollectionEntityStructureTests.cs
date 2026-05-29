using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Collections;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Features.Collections;

/// <summary>
/// TDD structural tests for Task 1: ContactAttemptEntity, DelinquencyNoteEntity,
/// ContactChannel enum, ContactOutcome enum, and DbSets on CardVaultDbContext.
/// RED: written before the entities and DbSets exist.
/// </summary>
public sealed class CollectionEntityStructureTests
{
    // ─────────────────────────────────────────────
    // ContactChannel enum
    // ─────────────────────────────────────────────

    [Fact]
    public void ContactChannel_HasPhoneValue()
        => Enum.GetNames<ContactChannel>().Should().Contain("Phone");

    [Fact]
    public void ContactChannel_HasEmailValue()
        => Enum.GetNames<ContactChannel>().Should().Contain("Email");

    [Fact]
    public void ContactChannel_HasSmsValue()
        => Enum.GetNames<ContactChannel>().Should().Contain("SMS");

    [Fact]
    public void ContactChannel_HasInPersonValue()
        => Enum.GetNames<ContactChannel>().Should().Contain("InPerson");

    // ─────────────────────────────────────────────
    // ContactOutcome enum
    // ─────────────────────────────────────────────

    [Fact]
    public void ContactOutcome_HasContactedValue()
        => Enum.GetNames<ContactOutcome>().Should().Contain("Contacted");

    [Fact]
    public void ContactOutcome_HasNoAnswerValue()
        => Enum.GetNames<ContactOutcome>().Should().Contain("NoAnswer");

    [Fact]
    public void ContactOutcome_HasInvalidContactValue()
        => Enum.GetNames<ContactOutcome>().Should().Contain("InvalidContact");

    [Fact]
    public void ContactOutcome_HasCustomerRefusedValue()
        => Enum.GetNames<ContactOutcome>().Should().Contain("CustomerRefused");

    // ─────────────────────────────────────────────
    // ContactAttemptEntity
    // ─────────────────────────────────────────────

    [Fact]
    public void ContactAttemptEntity_HasExpectedProperties()
    {
        var t = typeof(ContactAttemptEntity);
        t.GetProperty(nameof(ContactAttemptEntity.Id)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.DelinquencyRecordId)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.Channel)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.Outcome)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.Notes)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.AttemptedBy)).Should().NotBeNull();
        t.GetProperty(nameof(ContactAttemptEntity.AttemptedOn)).Should().NotBeNull();
    }

    [Fact]
    public void ContactAttemptEntity_DefaultId_IsNotEmpty()
    {
        var entity = new ContactAttemptEntity();
        entity.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void ContactAttemptEntity_DefaultAttemptedOn_IsRecent()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var entity = new ContactAttemptEntity();
        entity.AttemptedOn.Should().BeAfter(before);
    }

    // ─────────────────────────────────────────────
    // DelinquencyNoteEntity
    // ─────────────────────────────────────────────

    [Fact]
    public void DelinquencyNoteEntity_HasExpectedProperties()
    {
        var t = typeof(DelinquencyNoteEntity);
        t.GetProperty(nameof(DelinquencyNoteEntity.Id)).Should().NotBeNull();
        t.GetProperty(nameof(DelinquencyNoteEntity.DelinquencyRecordId)).Should().NotBeNull();
        t.GetProperty(nameof(DelinquencyNoteEntity.Content)).Should().NotBeNull();
        t.GetProperty(nameof(DelinquencyNoteEntity.CreatedBy)).Should().NotBeNull();
        t.GetProperty(nameof(DelinquencyNoteEntity.CreatedOn)).Should().NotBeNull();
    }

    [Fact]
    public void DelinquencyNoteEntity_DefaultId_IsNotEmpty()
    {
        var entity = new DelinquencyNoteEntity();
        entity.Id.Should().NotBeEmpty();
    }

    // ─────────────────────────────────────────────
    // DbSets present on CardVaultDbContext
    // ─────────────────────────────────────────────

    [Fact]
    public void CardVaultDbContext_HasContactAttemptsDbSet()
    {
        using var db = TestDbContextFactory.Create();
        db.ContactAttempts.Should().NotBeNull();
    }

    [Fact]
    public void CardVaultDbContext_HasDelinquencyNotesDbSet()
    {
        using var db = TestDbContextFactory.Create();
        db.DelinquencyNotes.Should().NotBeNull();
    }

    [Fact]
    public void ContactAttempts_CanBeInsertedAndQueried()
    {
        var dbName = $"T1_Contact_{Guid.NewGuid():N}";
        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.ContactAttempts.Add(new ContactAttemptEntity
            {
                DelinquencyRecordId = Guid.NewGuid(),
                Channel = ContactChannel.Phone,
                Outcome = ContactOutcome.Contacted,
                Notes = "Called customer, promised payment.",
                AttemptedBy = "agent@bank.com",
            });
            db.SaveChanges();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        db2.ContactAttempts.Should().HaveCount(1);
    }

    [Fact]
    public void DelinquencyNotes_CanBeInsertedAndQueried()
    {
        var dbName = $"T1_Note_{Guid.NewGuid():N}";
        using (var db = TestDbContextFactory.Create(dbName))
        {
            db.DelinquencyNotes.Add(new DelinquencyNoteEntity
            {
                DelinquencyRecordId = Guid.NewGuid(),
                Content = "Customer showed willingness to pay next Friday.",
                CreatedBy = "agent@bank.com",
            });
            db.SaveChanges();
        }

        using var db2 = TestDbContextFactory.CreateSecondContext(dbName);
        db2.DelinquencyNotes.Should().HaveCount(1);
    }
}
