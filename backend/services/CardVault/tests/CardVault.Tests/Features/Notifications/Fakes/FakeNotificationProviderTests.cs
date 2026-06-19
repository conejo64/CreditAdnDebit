using CardVault.Infrastructure.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.Fakes;

public sealed class FakeNotificationProviderTests
{
    private static NotificationSendRequest SmsRequest(Guid? deliveryId = null)
        => new(
            DeliveryId: deliveryId ?? Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Channel: NotificationChannel.Sms,
            Destination: "+15550001111",
            RenderedSubject: "OTP",
            RenderedBody: "Your code is 123456",
            TemplateType: "Otp",
            Locale: "es-EC");

    private static NotificationSendRequest EmailRequest()
        => new(
            DeliveryId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Channel: NotificationChannel.Email,
            Destination: "test@example.com",
            RenderedSubject: "Hello",
            RenderedBody: "<p>Body</p>",
            TemplateType: "TransactionNotification",
            Locale: "en-US");

    // ── FakeNotificationProvider contract ─────────────────────────────────────

    [Fact]
    public void FakeNotificationProvider_ProviderId_IsFake()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms);
        fake.ProviderId.Should().Be("fake");
    }

    [Fact]
    public void FakeNotificationProvider_Channel_MatchesConstructorArg()
    {
        var sms = new FakeNotificationProvider(NotificationChannel.Sms);
        sms.Channel.Should().Be(NotificationChannel.Sms);

        var email = new FakeNotificationProvider(NotificationChannel.Email);
        email.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public void FakeNotificationProvider_CanHandle_AlwaysTrue()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms);
        fake.CanHandle("+15550001111").Should().BeTrue();
        fake.CanHandle("test@example.com").Should().BeTrue();
        fake.CanHandle(string.Empty).Should().BeTrue();
    }

    // ── Default outcome = Accepted ────────────────────────────────────────────

    [Fact]
    public async Task FakeNotificationProvider_Default_ReturnsAccepted()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms);
        var result = await fake.SendAsync(SmsRequest(), CancellationToken.None);
        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().NotBeNullOrEmpty();
    }

    // ── Configurable outcome queue ─────────────────────────────────────────────

    [Fact]
    public async Task FakeNotificationProvider_WithTransientFailureQueued_ReturnsTransientFailure()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms,
            ProviderOutcome.TransientFailure);

        var result = await fake.SendAsync(SmsRequest(), CancellationToken.None);
        result.Outcome.Should().Be(ProviderOutcome.TransientFailure);
    }

    [Fact]
    public async Task FakeNotificationProvider_WithPermanentFailureQueued_ReturnsPermanentFailure()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms,
            ProviderOutcome.PermanentFailure);

        var result = await fake.SendAsync(SmsRequest(), CancellationToken.None);
        result.Outcome.Should().Be(ProviderOutcome.PermanentFailure);
    }

    [Fact]
    public async Task FakeNotificationProvider_OutcomeQueueDequeuedPerCall()
    {
        // Queue: Transient, Transient, Accepted → retry-then-Sent scenario
        var fake = new FakeNotificationProvider(NotificationChannel.Sms,
            ProviderOutcome.TransientFailure,
            ProviderOutcome.TransientFailure,
            ProviderOutcome.Accepted);

        var req = SmsRequest();
        var r1 = await fake.SendAsync(req, CancellationToken.None);
        var r2 = await fake.SendAsync(req, CancellationToken.None);
        var r3 = await fake.SendAsync(req, CancellationToken.None);

        r1.Outcome.Should().Be(ProviderOutcome.TransientFailure);
        r2.Outcome.Should().Be(ProviderOutcome.TransientFailure);
        r3.Outcome.Should().Be(ProviderOutcome.Accepted);
    }

    [Fact]
    public async Task FakeNotificationProvider_QueueExhausted_FallsBackToAccepted()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms,
            ProviderOutcome.TransientFailure);

        var req = SmsRequest();
        await fake.SendAsync(req, CancellationToken.None); // dequeues TransientFailure
        var second = await fake.SendAsync(req, CancellationToken.None); // queue empty → fallback
        second.Outcome.Should().Be(ProviderOutcome.Accepted);
    }

    // ── Call recording ────────────────────────────────────────────────────────

    [Fact]
    public async Task FakeNotificationProvider_RecordsCalls()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Sms);
        var req1 = SmsRequest();
        var req2 = SmsRequest();

        await fake.SendAsync(req1, CancellationToken.None);
        await fake.SendAsync(req2, CancellationToken.None);

        fake.Calls.Should().HaveCount(2);
        fake.Calls[0].Should().Be(req1);
        fake.Calls[1].Should().Be(req2);
    }

    [Fact]
    public void FakeNotificationProvider_InitialCallsIsEmpty()
    {
        var fake = new FakeNotificationProvider(NotificationChannel.Email);
        fake.Calls.Should().BeEmpty();
    }

    // ── FakeProviderRegistry ──────────────────────────────────────────────────

    [Fact]
    public void FakeProviderRegistry_ResolvesProvidersForSms()
    {
        var fakeSms = new FakeNotificationProvider(NotificationChannel.Sms);
        var registry = new FakeProviderRegistry(fakeSms);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");

        chain.Should().HaveCount(1);
        chain[0].Should().BeSameAs(fakeSms);
    }

    [Fact]
    public void FakeProviderRegistry_ResolvesProvidersForEmail()
    {
        var fakeEmail = new FakeNotificationProvider(NotificationChannel.Email);
        var registry = new FakeProviderRegistry(fakeEmail);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Email, "test@example.com");

        chain.Should().HaveCount(1);
        chain[0].Should().BeSameAs(fakeEmail);
    }

    [Fact]
    public void FakeProviderRegistry_ReturnsProvidersForAnyChannel()
    {
        var fakeAny = new FakeNotificationProvider(NotificationChannel.Sms);
        var registry = new FakeProviderRegistry(fakeAny);

        // Registry is a test double — returns its provider regardless of channel filter
        var smsChain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");
        smsChain.Should().NotBeEmpty();
    }

    [Fact]
    public void FakeProviderRegistry_MultipleProviders_ReturnsAllInOrder()
    {
        var fake1 = new FakeNotificationProvider(NotificationChannel.Sms);
        var fake2 = new FakeNotificationProvider(NotificationChannel.Sms);
        var registry = new FakeProviderRegistry(fake1, fake2);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");

        chain.Should().HaveCount(2);
        chain[0].Should().BeSameAs(fake1);
        chain[1].Should().BeSameAs(fake2);
    }
}
