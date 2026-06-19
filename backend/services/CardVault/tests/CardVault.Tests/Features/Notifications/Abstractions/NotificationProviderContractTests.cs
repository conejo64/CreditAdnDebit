using CardVault.Infrastructure.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;

namespace CardVault.Tests.Features.Notifications.Abstractions;

public sealed class NotificationProviderContractTests
{
    // ── ProviderOutcome enum ─────────────────────────────────────────────────

    [Fact]
    public void ProviderOutcome_HasExpectedValues()
    {
        var values = Enum.GetValues<ProviderOutcome>();
        values.Should().Contain(ProviderOutcome.Accepted);
        values.Should().Contain(ProviderOutcome.TransientFailure);
        values.Should().Contain(ProviderOutcome.PermanentFailure);
    }

    // ── ProviderSendResult record ─────────────────────────────────────────────

    [Fact]
    public void ProviderSendResult_Accepted_CanBeConstructed()
    {
        var result = new ProviderSendResult(
            Outcome: ProviderOutcome.Accepted,
            ProviderReference: "msg-123",
            ErrorCode: null,
            ErrorMessage: null,
            ProviderReportedAt: null);

        result.Outcome.Should().Be(ProviderOutcome.Accepted);
        result.ProviderReference.Should().Be("msg-123");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void ProviderSendResult_TransientFailure_CanBeConstructed()
    {
        var result = new ProviderSendResult(
            Outcome: ProviderOutcome.TransientFailure,
            ProviderReference: null,
            ErrorCode: "5xx",
            ErrorMessage: "Internal server error",
            ProviderReportedAt: DateTimeOffset.UtcNow);

        result.Outcome.Should().Be(ProviderOutcome.TransientFailure);
        result.ProviderReference.Should().BeNull();
        result.ErrorCode.Should().Be("5xx");
    }

    [Fact]
    public void ProviderSendResult_SupportsValueEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new ProviderSendResult(ProviderOutcome.Accepted, "ref", null, null, ts);
        var b = new ProviderSendResult(ProviderOutcome.Accepted, "ref", null, null, ts);
        a.Should().Be(b);
    }

    // ── NotificationSendRequest record ────────────────────────────────────────

    [Fact]
    public void NotificationSendRequest_CanBeConstructed()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var request = new NotificationSendRequest(
            DeliveryId: id,
            TenantId: tenantId,
            Channel: NotificationChannel.Email,
            Destination: "test@example.com",
            RenderedSubject: "Hello",
            RenderedBody: "<p>Body</p>",
            TemplateType: "Otp",
            Locale: "es-EC");

        request.DeliveryId.Should().Be(id);
        request.TenantId.Should().Be(tenantId);
        request.Channel.Should().Be(NotificationChannel.Email);
        request.Destination.Should().Be("test@example.com");
        request.RenderedSubject.Should().Be("Hello");
        request.RenderedBody.Should().Be("<p>Body</p>");
        request.TemplateType.Should().Be("Otp");
        request.Locale.Should().Be("es-EC");
    }

    [Fact]
    public void NotificationSendRequest_SupportsValueEquality()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var a = new NotificationSendRequest(id, tenantId, NotificationChannel.Sms, "+15550001111", "S", "B", "Otp", "en-US");
        var b = new NotificationSendRequest(id, tenantId, NotificationChannel.Sms, "+15550001111", "S", "B", "Otp", "en-US");
        a.Should().Be(b);
    }

    // ── INotificationProvider interface contract ──────────────────────────────

    [Fact]
    public void INotificationProvider_ContractIsSatisfiable()
    {
        // Verify the interface can be resolved from a stub implementation
        INotificationProvider provider = new StubProvider();
        provider.ProviderId.Should().Be("stub");
        provider.Channel.Should().Be(NotificationChannel.Sms);
        provider.CanHandle("+15550001111").Should().BeTrue();
    }

    [Fact]
    public async Task INotificationProvider_SendAsync_ReturnsResult()
    {
        INotificationProvider provider = new StubProvider();
        var request = new NotificationSendRequest(
            Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Sms,
            "+15550001111", "S", "B", "Otp", "es-EC");

        var result = await provider.SendAsync(request, CancellationToken.None);
        result.Should().NotBeNull();
        result.Outcome.Should().Be(ProviderOutcome.Accepted);
    }

    // ── INotificationProviderRegistry contract ────────────────────────────────

    [Fact]
    public void INotificationProviderRegistry_ResolveChain_ReturnsReadOnlyList()
    {
        INotificationProviderRegistry registry = new StubRegistry(new StubProvider());
        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");
        chain.Should().NotBeNull();
        chain.Should().HaveCount(1);
    }

    // ── INotificationDispatcher contract ─────────────────────────────────────

    [Fact]
    public async Task INotificationDispatcher_DispatchBatchAsync_ReturnsInt()
    {
        INotificationDispatcher dispatcher = new StubDispatcher();
        var count = await dispatcher.DispatchBatchAsync(10, CancellationToken.None);
        count.Should().Be(0);
    }

    // ── IDeliveryStateMachine contract ────────────────────────────────────────

    [Fact]
    public void IDeliveryStateMachine_CanTransition_IsCallable()
    {
        IDeliveryStateMachine fsm = new StubStateMachine();
        var canTransition = fsm.CanTransition(NotificationDeliveryStatus.Pending, NotificationDeliveryStatus.Sending);
        canTransition.Should().BeTrue();
    }

    // ── IWebhookSignatureValidator contract ───────────────────────────────────

    [Fact]
    public void IWebhookSignatureValidator_ProviderId_IsAccessible()
    {
        IWebhookSignatureValidator validator = new StubWebhookValidator();
        validator.ProviderId.Should().Be("stub");
    }
}

// ── Stub implementations for contract verification ───────────────────────────

file sealed class StubProvider : INotificationProvider
{
    public string ProviderId => "stub";
    public NotificationChannel Channel => NotificationChannel.Sms;
    public bool CanHandle(string destinationE164OrEmail) => true;
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
        => Task.FromResult(new ProviderSendResult(ProviderOutcome.Accepted, "ref-stub", null, null, null));
}

file sealed class StubRegistry : INotificationProviderRegistry
{
    private readonly INotificationProvider _provider;
    public StubRegistry(INotificationProvider provider) => _provider = provider;
    public IReadOnlyList<INotificationProvider> ResolveChain(Guid tenantId, NotificationChannel channel, string destination)
        => new[] { _provider };
}

file sealed class StubDispatcher : INotificationDispatcher
{
    public Task<int> DispatchBatchAsync(int take, CancellationToken ct) => Task.FromResult(0);
}

file sealed class StubStateMachine : IDeliveryStateMachine
{
    public bool CanTransition(NotificationDeliveryStatus from, NotificationDeliveryStatus to) => true;
    public void Transition(CustomerNotificationDeliveryEntity d, NotificationDeliveryStatus to) { }
    public DateTimeOffset ComputeNextAttempt(int attempts, DateTimeOffset now) => now.AddSeconds(30);
}

file sealed class StubWebhookValidator : IWebhookSignatureValidator
{
    public string ProviderId => "stub";
    public string SignatureHeaderName => "X-Stub-Signature";
    public WebhookValidationResult Validate(Microsoft.AspNetCore.Http.HttpRequest request, ReadOnlySpan<byte> rawBody)
        => WebhookValidationResult.Valid;
}
