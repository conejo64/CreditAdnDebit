using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.StateMachine;

public sealed class DeliveryStateMachineTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    private static DeliveryStateMachine CreateFsm(DateTimeOffset? clock = null)
    {
        var fixedTime = clock ?? FixedNow;
        return new DeliveryStateMachine(() => fixedTime);
    }

    private static CustomerNotificationDeliveryEntity NewDelivery(
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Pending)
        => new()
        {
            Id = Guid.NewGuid(),
            NotificationId = Guid.NewGuid(),
            Channel = NotificationChannel.Email,
            DestinationMasked = "te***@example.com",
            DestinationHash = "abc123",
            Status = status
        };

    // ── Legal transitions ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.Sent)]
    [InlineData(NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.DeadLetter)]
    [InlineData(NotificationDeliveryStatus.Sending,    NotificationDeliveryStatus.DeadLetter)]
    public void CanTransition_LegalTransitions_ReturnsTrue(
        NotificationDeliveryStatus from, NotificationDeliveryStatus to)
    {
        var fsm = CreateFsm();
        fsm.CanTransition(from, to).Should().BeTrue();
    }

    // ── Illegal transitions ───────────────────────────────────────────────────

    [Theory]
    [InlineData(NotificationDeliveryStatus.Sent,       NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.Sent,       NotificationDeliveryStatus.Pending)]
    [InlineData(NotificationDeliveryStatus.Sent,       NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Sent,       NotificationDeliveryStatus.DeadLetter)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, NotificationDeliveryStatus.Pending)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, NotificationDeliveryStatus.Sent)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.Sent)]
    [InlineData(NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.Failed)]
    [InlineData(NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.DeadLetter)]
    [InlineData(NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.Sent)]
    [InlineData(NotificationDeliveryStatus.Failed,     NotificationDeliveryStatus.Pending)]
    public void CanTransition_IllegalTransitions_ReturnsFalse(
        NotificationDeliveryStatus from, NotificationDeliveryStatus to)
    {
        var fsm = CreateFsm();
        fsm.CanTransition(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(NotificationDeliveryStatus.Sent,       NotificationDeliveryStatus.Sending)]
    [InlineData(NotificationDeliveryStatus.DeadLetter, NotificationDeliveryStatus.Pending)]
    [InlineData(NotificationDeliveryStatus.Pending,    NotificationDeliveryStatus.Sent)]
    public void Transition_IllegalTransition_ThrowsInvalidDeliveryTransitionException(
        NotificationDeliveryStatus from, NotificationDeliveryStatus to)
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(from);

        var act = () => fsm.Transition(delivery, to);

        act.Should().Throw<InvalidDeliveryTransitionException>()
            .Which.DeliveryId.Should().Be(delivery.Id);
    }

    [Fact]
    public void Transition_IllegalTransition_Exception_CarriesFromAndTo()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Sent);

        var act = () => fsm.Transition(delivery, NotificationDeliveryStatus.Pending);

        act.Should().Throw<InvalidDeliveryTransitionException>()
            .Which.From.Should().Be(NotificationDeliveryStatus.Sent);

        act.Should().Throw<InvalidDeliveryTransitionException>()
            .Which.To.Should().Be(NotificationDeliveryStatus.Pending);
    }

    // ── Transition side effects ───────────────────────────────────────────────

    [Fact]
    public void Transition_PendingToSending_SetsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Pending);

        fsm.Transition(delivery, NotificationDeliveryStatus.Sending);

        delivery.Status.Should().Be(NotificationDeliveryStatus.Sending);
        delivery.SendingStartedOn.Should().Be(FixedNow);
    }

    [Fact]
    public void Transition_FailedToSending_SetsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Failed);

        fsm.Transition(delivery, NotificationDeliveryStatus.Sending);

        delivery.Status.Should().Be(NotificationDeliveryStatus.Sending);
        delivery.SendingStartedOn.Should().Be(FixedNow);
    }

    [Fact]
    public void Transition_SendingToSent_ClearsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Sending);
        delivery.SendingStartedOn = FixedNow.AddMinutes(-1);

        fsm.Transition(delivery, NotificationDeliveryStatus.Sent);

        delivery.Status.Should().Be(NotificationDeliveryStatus.Sent);
        delivery.SendingStartedOn.Should().BeNull();
    }

    [Fact]
    public void Transition_SendingToFailed_ClearsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Sending);
        delivery.SendingStartedOn = FixedNow.AddMinutes(-1);

        fsm.Transition(delivery, NotificationDeliveryStatus.Failed);

        delivery.Status.Should().Be(NotificationDeliveryStatus.Failed);
        delivery.SendingStartedOn.Should().BeNull();
    }

    [Fact]
    public void Transition_SendingToDeadLetter_ClearsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Sending);
        delivery.SendingStartedOn = FixedNow.AddSeconds(-30);

        fsm.Transition(delivery, NotificationDeliveryStatus.DeadLetter);

        delivery.Status.Should().Be(NotificationDeliveryStatus.DeadLetter);
        delivery.SendingStartedOn.Should().BeNull();
    }

    [Fact]
    public void Transition_FailedToDeadLetter_ClearsSendingStartedOn()
    {
        var fsm = CreateFsm();
        var delivery = NewDelivery(NotificationDeliveryStatus.Failed);
        delivery.SendingStartedOn = null; // failed never has it set

        fsm.Transition(delivery, NotificationDeliveryStatus.DeadLetter);

        delivery.Status.Should().Be(NotificationDeliveryStatus.DeadLetter);
        delivery.SendingStartedOn.Should().BeNull();
    }

    // ── ComputeNextAttempt — backoff with jitter ──────────────────────────────

    [Fact]
    public void ComputeNextAttempt_Attempt1_Returns30SecondsWithinPlusMinus10Percent()
    {
        var fsm = CreateFsm();
        const int runs = 200;
        var lowerBound = FixedNow.AddSeconds(30 * 0.90);
        var upperBound = FixedNow.AddSeconds(30 * 1.10);

        for (var i = 0; i < runs; i++)
        {
            var result = fsm.ComputeNextAttempt(1, FixedNow);
            result.Should().BeOnOrAfter(lowerBound,
                "attempt 1 should be at least now + 27s (30s - 10%)");
            result.Should().BeOnOrBefore(upperBound,
                "attempt 1 should be at most now + 33s (30s + 10%)");
        }
    }

    [Fact]
    public void ComputeNextAttempt_Attempt2_Returns2MinutesWithinPlusMinus10Percent()
    {
        var fsm = CreateFsm();
        const int runs = 200;
        const double baseSeconds = 2 * 60;
        var lowerBound = FixedNow.AddSeconds(baseSeconds * 0.90);
        var upperBound = FixedNow.AddSeconds(baseSeconds * 1.10);

        for (var i = 0; i < runs; i++)
        {
            var result = fsm.ComputeNextAttempt(2, FixedNow);
            result.Should().BeOnOrAfter(lowerBound,
                "attempt 2 should be at least now + 108s (2m - 10%)");
            result.Should().BeOnOrBefore(upperBound,
                "attempt 2 should be at most now + 132s (2m + 10%)");
        }
    }

    [Fact]
    public void ComputeNextAttempt_Attempt1_ShowsJitter_AcrossMultipleRuns()
    {
        var fsm = CreateFsm();
        var results = Enumerable.Range(0, 100)
            .Select(_ => fsm.ComputeNextAttempt(1, FixedNow))
            .Distinct()
            .ToList();

        // Should have variation — not all the same value
        results.Count.Should().BeGreaterThan(1,
            "jitter should produce different values across calls");
    }
}
