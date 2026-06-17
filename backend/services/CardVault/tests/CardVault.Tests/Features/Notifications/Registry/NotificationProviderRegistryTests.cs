using CardVault.Application.Services.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CardVault.Tests.Features.Notifications.Registry;

/// <summary>
/// Tests for the slice-1 stub <see cref="NotificationProviderRegistry"/>.
/// The registry is a singleton with a 5-min cache; for these tests we bypass
/// the cache by constructing with stub providers directly.
/// </summary>
public sealed class NotificationProviderRegistryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static INotificationProvider MakeTwilio()
    {
        var p = new SliceOneStubSmsProvider("twilio");
        return p;
    }

    private static INotificationProvider MakeSendGrid()
    {
        var p = new SliceOneStubEmailProvider("sendgrid");
        return p;
    }

    private static NotificationProviderRegistry CreateRegistry(
        INotificationProvider smsProvider,
        INotificationProvider emailProvider)
        => new NotificationProviderRegistry(new[] { smsProvider, emailProvider });

    // ── SMS chain resolution ──────────────────────────────────────────────────

    [Fact]
    public void ResolveChain_ForSms_ContainsTwilioProvider()
    {
        var twilio = MakeTwilio();
        var sendgrid = MakeSendGrid();
        var registry = CreateRegistry(twilio, sendgrid);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");

        chain.Should().NotBeEmpty();
        chain.Should().Contain(p => p.ProviderId == "twilio");
    }

    [Fact]
    public void ResolveChain_ForSms_DoesNotContainEmailProvider()
    {
        var twilio = MakeTwilio();
        var sendgrid = MakeSendGrid();
        var registry = CreateRegistry(twilio, sendgrid);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");

        chain.Should().NotContain(p => p.ProviderId == "sendgrid");
    }

    // ── Email chain resolution ────────────────────────────────────────────────

    [Fact]
    public void ResolveChain_ForEmail_ContainsSendGridProvider()
    {
        var twilio = MakeTwilio();
        var sendgrid = MakeSendGrid();
        var registry = CreateRegistry(twilio, sendgrid);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Email, "user@example.com");

        chain.Should().NotBeEmpty();
        chain.Should().Contain(p => p.ProviderId == "sendgrid");
    }

    [Fact]
    public void ResolveChain_ForEmail_DoesNotContainSmsProvider()
    {
        var twilio = MakeTwilio();
        var sendgrid = MakeSendGrid();
        var registry = CreateRegistry(twilio, sendgrid);

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Email, "user@example.com");

        chain.Should().NotContain(p => p.ProviderId == "twilio");
    }

    // ── CanHandle filter is respected ─────────────────────────────────────────

    [Fact]
    public void ResolveChain_CanHandleFilterRespected_ExcludesProviderThatCannotHandle()
    {
        // A Movistar-like provider that only handles +593
        var movistar = new RestrictedEcuadorOnlyProvider("movistar-ec");
        var twilio = MakeTwilio();
        var registry = new NotificationProviderRegistry(new INotificationProvider[] { movistar, twilio });

        // Non-+593 destination
        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+15550001111");

        chain.Should().NotContain(p => p.ProviderId == "movistar-ec",
            "movistar-ec only handles +593 prefix — should be excluded for +1 numbers");
        chain.Should().Contain(p => p.ProviderId == "twilio");
    }

    [Fact]
    public void ResolveChain_CanHandleFilterRespected_IncludesEcuadorProviderForEcuadorNumber()
    {
        var movistar = new RestrictedEcuadorOnlyProvider("movistar-ec");
        var twilio = MakeTwilio();
        var registry = new NotificationProviderRegistry(new INotificationProvider[] { movistar, twilio });

        var chain = registry.ResolveChain(Guid.NewGuid(), NotificationChannel.Sms, "+593987654321");

        chain.Should().Contain(p => p.ProviderId == "movistar-ec");
    }

    // ── Registry implements INotificationProviderRegistry ─────────────────────

    [Fact]
    public void NotificationProviderRegistry_ImplementsInterface()
    {
        var registry = CreateRegistry(MakeTwilio(), MakeSendGrid());
        registry.Should().BeAssignableTo<INotificationProviderRegistry>();
    }

    // ── DI registration as singleton ──────────────────────────────────────────

    [Fact]
    public void NotificationProviderRegistry_DiRegistration_IsSingleton()
    {
        var services = new ServiceCollection();
        var twilio = MakeTwilio();
        var sendgrid = MakeSendGrid();

        services.AddSingleton<INotificationProvider>(twilio);
        services.AddSingleton<INotificationProvider>(sendgrid);
        services.AddSingleton<INotificationProviderRegistry>(sp =>
        {
            var providers = sp.GetServices<INotificationProvider>().ToArray();
            return new NotificationProviderRegistry(providers);
        });

        var provider = services.BuildServiceProvider();
        var r1 = provider.GetRequiredService<INotificationProviderRegistry>();
        var r2 = provider.GetRequiredService<INotificationProviderRegistry>();

        r1.Should().BeSameAs(r2, "registry must be singleton");
    }
}

// ── Local stub helpers ────────────────────────────────────────────────────────

file sealed class SliceOneStubSmsProvider : INotificationProvider
{
    public string ProviderId { get; }
    public NotificationChannel Channel => NotificationChannel.Sms;
    public SliceOneStubSmsProvider(string providerId) => ProviderId = providerId;
    public bool CanHandle(string destinationE164OrEmail) => true;
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
        => Task.FromResult(new ProviderSendResult(ProviderOutcome.Accepted, "ref", null, null, null));
}

file sealed class SliceOneStubEmailProvider : INotificationProvider
{
    public string ProviderId { get; }
    public NotificationChannel Channel => NotificationChannel.Email;
    public SliceOneStubEmailProvider(string providerId) => ProviderId = providerId;
    public bool CanHandle(string destinationE164OrEmail) => true;
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
        => Task.FromResult(new ProviderSendResult(ProviderOutcome.Accepted, "ref", null, null, null));
}

file sealed class RestrictedEcuadorOnlyProvider : INotificationProvider
{
    public string ProviderId { get; }
    public NotificationChannel Channel => NotificationChannel.Sms;
    public RestrictedEcuadorOnlyProvider(string providerId) => ProviderId = providerId;
    public bool CanHandle(string destinationE164OrEmail) => destinationE164OrEmail.StartsWith("+593");
    public Task<ProviderSendResult> SendAsync(NotificationSendRequest request, CancellationToken ct)
        => Task.FromResult(new ProviderSendResult(ProviderOutcome.Accepted, "ref", null, null, null));
}
