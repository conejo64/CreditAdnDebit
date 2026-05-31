using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.StateMachine;

public sealed class DeliveryStatusEnumTests
{
    // ── NotificationDeliveryStatus enum extension ────────────────────────────

    [Fact]
    public void NotificationDeliveryStatus_ExistingValues_AreNotRenumbered()
    {
        // CRITICAL: must never renumber existing values
        ((int)NotificationDeliveryStatus.Pending).Should().Be(1);
        ((int)NotificationDeliveryStatus.Sent).Should().Be(2);
        ((int)NotificationDeliveryStatus.Failed).Should().Be(3);
    }

    [Fact]
    public void NotificationDeliveryStatus_NewValues_ExistAtCorrectPositions()
    {
        ((int)NotificationDeliveryStatus.Sending).Should().Be(4);
        ((int)NotificationDeliveryStatus.DeadLetter).Should().Be(5);
    }

    [Fact]
    public void NotificationDeliveryStatus_HasExactlyFiveValues()
    {
        var values = Enum.GetValues<NotificationDeliveryStatus>();
        values.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(NotificationDeliveryStatus.Pending, 1)]
    [InlineData(NotificationDeliveryStatus.Sent, 2)]
    [InlineData(NotificationDeliveryStatus.Failed, 3)]
    [InlineData(NotificationDeliveryStatus.Sending, 4)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, 5)]
    public void NotificationDeliveryStatus_AllValuesHaveExpectedIntegerRepresentation(
        NotificationDeliveryStatus status, int expected)
    {
        ((int)status).Should().Be(expected);
    }

    // ── InvalidDeliveryTransitionException ───────────────────────────────────

    [Fact]
    public void InvalidDeliveryTransitionException_CanBeConstructed_WithAllFields()
    {
        var deliveryId = Guid.NewGuid();
        var ex = new InvalidDeliveryTransitionException(
            deliveryId: deliveryId,
            from: NotificationDeliveryStatus.Sent,
            to: NotificationDeliveryStatus.Pending,
            caller: "TestCaller");

        ex.DeliveryId.Should().Be(deliveryId);
        ex.From.Should().Be(NotificationDeliveryStatus.Sent);
        ex.To.Should().Be(NotificationDeliveryStatus.Pending);
        ex.Caller.Should().Be("TestCaller");
    }

    [Fact]
    public void InvalidDeliveryTransitionException_IsException()
    {
        var ex = new InvalidDeliveryTransitionException(
            Guid.NewGuid(),
            NotificationDeliveryStatus.DeadLetter,
            NotificationDeliveryStatus.Pending,
            "Test");

        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void InvalidDeliveryTransitionException_Message_ContainsFromAndToStates()
    {
        var ex = new InvalidDeliveryTransitionException(
            Guid.NewGuid(),
            NotificationDeliveryStatus.Sent,
            NotificationDeliveryStatus.Sending,
            "MySender");

        ex.Message.Should().Contain("Sent");
        ex.Message.Should().Contain("Sending");
    }

    [Fact]
    public void InvalidDeliveryTransitionException_Message_ContainsCaller()
    {
        var ex = new InvalidDeliveryTransitionException(
            Guid.NewGuid(),
            NotificationDeliveryStatus.DeadLetter,
            NotificationDeliveryStatus.Pending,
            "MyCallerMethod");

        ex.Message.Should().Contain("MyCallerMethod");
    }

    [Fact]
    public void InvalidDeliveryTransitionException_Message_ContainsDeliveryId()
    {
        var deliveryId = Guid.NewGuid();
        var ex = new InvalidDeliveryTransitionException(
            deliveryId,
            NotificationDeliveryStatus.Sent,
            NotificationDeliveryStatus.Pending,
            "caller");

        ex.Message.Should().Contain(deliveryId.ToString());
    }
}
