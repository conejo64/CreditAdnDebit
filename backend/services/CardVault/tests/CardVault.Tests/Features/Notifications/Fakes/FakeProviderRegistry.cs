using CardVault.Api.Services.Notifications;
using CardVault.Infrastructure.Persistence.Notifications;

namespace CardVault.Tests.Features.Notifications.Fakes;

/// <summary>
/// Test double for <see cref="INotificationProviderRegistry"/>.
/// Returns all registered fake providers for any channel — enabling full dispatcher
/// fault injection without real HTTP calls.
/// </summary>
public sealed class FakeProviderRegistry : INotificationProviderRegistry
{
    private readonly IReadOnlyList<INotificationProvider> _providers;

    /// <summary>
    /// Creates a registry returning the given providers (in order) for any channel.
    /// </summary>
    public FakeProviderRegistry(params INotificationProvider[] providers)
    {
        _providers = providers;
    }

    /// <inheritdoc />
    public IReadOnlyList<INotificationProvider> ResolveChain(
        Guid tenantId,
        NotificationChannel channel,
        string destination)
        => _providers;
}
