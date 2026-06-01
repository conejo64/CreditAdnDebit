using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Tests.Features.Notifications.Persistence;

/// <summary>
/// Task 1d.1 — EF entity delta tests (model validation, no real DB required).
/// Uses in-memory provider to validate EF model metadata.
/// </summary>
public sealed class NotificationDeliveryEntityTests
{
    private static CardVaultDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<CardVaultDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CardVaultDbContext(opts);
    }

    // ────────────────────────────────────────────────────────────────────
    // Property existence tests
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Entity_HasNextAttemptOn_NullableDateTimeOffset()
    {
        var type = typeof(CustomerNotificationDeliveryEntity);
        var prop = type.GetProperty(nameof(CustomerNotificationDeliveryEntity.NextAttemptOn));

        prop.Should().NotBeNull("NextAttemptOn property must exist");
        prop!.PropertyType.Should().Be(typeof(DateTimeOffset?), "NextAttemptOn must be nullable DateTimeOffset");
    }

    [Fact]
    public void Entity_HasSendingStartedOn_NullableDateTimeOffset()
    {
        var type = typeof(CustomerNotificationDeliveryEntity);
        var prop = type.GetProperty(nameof(CustomerNotificationDeliveryEntity.SendingStartedOn));

        prop.Should().NotBeNull("SendingStartedOn property must exist");
        prop!.PropertyType.Should().Be(typeof(DateTimeOffset?), "SendingStartedOn must be nullable DateTimeOffset");
    }

    [Fact]
    public void Entity_HasProviderId_NullableStringWithMaxLength32()
    {
        var type = typeof(CustomerNotificationDeliveryEntity);
        var prop = type.GetProperty(nameof(CustomerNotificationDeliveryEntity.ProviderId));

        prop.Should().NotBeNull("ProviderId property must exist");
        prop!.PropertyType.Should().Be(typeof(string), "ProviderId must be string");

        // MaxLength 32 is enforced via EF config (not data annotation), so check EF metadata
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var efProp = entityType.FindProperty(nameof(CustomerNotificationDeliveryEntity.ProviderId))!;
        efProp.GetMaxLength().Should().Be(32, "ProviderId must have MaxLength(32) in EF config");
    }

    [Fact]
    public void Entity_HasTenantId_Guid()
    {
        var type = typeof(CustomerNotificationDeliveryEntity);
        var prop = type.GetProperty(nameof(CustomerNotificationDeliveryEntity.TenantId));

        prop.Should().NotBeNull("TenantId property must exist");
        prop!.PropertyType.Should().Be(typeof(Guid), "TenantId must be Guid");
    }

    // ────────────────────────────────────────────────────────────────────
    // EF model metadata tests (index existence)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void EfModel_HasIndex_Status_NextAttemptOn()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var indexes = entityType.GetIndexes().ToList();

        indexes.Should().Contain(
            idx => idx.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "Status", "NextAttemptOn" }),
            "compound index (Status, NextAttemptOn) must exist for claim query");
    }

    [Fact]
    public void EfModel_HasIndex_Status_SendingStartedOn()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var indexes = entityType.GetIndexes().ToList();

        indexes.Should().Contain(
            idx => idx.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "Status", "SendingStartedOn" }),
            "compound index (Status, SendingStartedOn) must exist for crash-sweep query");
    }

    [Fact]
    public void EfModel_HasIndex_TenantId()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var indexes = entityType.GetIndexes().ToList();

        indexes.Should().Contain(
            idx => idx.Properties.Count == 1 && idx.Properties[0].Name == "TenantId",
            "single-column index on TenantId must exist");
    }

    [Fact]
    public void EfModel_ExistingIndex_Status_CreatedOn_StillPresent()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var indexes = entityType.GetIndexes().ToList();

        indexes.Should().Contain(
            idx => idx.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "Status", "CreatedOn" }),
            "existing index (Status, CreatedOn) must NOT be removed by migration");
    }

    [Fact]
    public void EfModel_ExistingUniqueIndex_NotificationId_Channel_StillPresent()
    {
        using var db = CreateDb();
        var entityType = db.Model.FindEntityType(typeof(CustomerNotificationDeliveryEntity))!;
        var indexes = entityType.GetIndexes().ToList();

        indexes.Should().Contain(
            idx => idx.IsUnique &&
                   idx.Properties.Select(p => p.Name)
                       .SequenceEqual(new[] { "NotificationId", "Channel" }),
            "existing unique index (NotificationId, Channel) must NOT be removed");
    }

    // ────────────────────────────────────────────────────────────────────
    // TenantId default value test
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Entity_TenantId_DefaultsToGuidEmpty_WhenNotSet()
    {
        // IMPORTANT: Guid.Empty is the safe default for single-tenant backfill.
        // Multi-tenant backfill is a separate operational step.
        var delivery = new CustomerNotificationDeliveryEntity();
        delivery.TenantId.Should().Be(Guid.Empty,
            "TenantId default must be Guid.Empty; multi-tenant backfill is a separate operational step");
    }
}
