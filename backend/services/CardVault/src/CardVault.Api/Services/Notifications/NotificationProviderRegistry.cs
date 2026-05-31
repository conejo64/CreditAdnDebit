using System.Collections.Concurrent;
using CardVault.Infrastructure.Persistence.Notifications;

namespace CardVault.Api.Services.Notifications;

/// <summary>
/// Slice-1 stub implementation of <see cref="INotificationProviderRegistry"/>.
/// <para>
/// Returns a fixed provider chain filtered by <see cref="INotificationProvider.CanHandle"/>:
/// <list type="bullet">
///   <item>SMS → all registered SMS providers that can handle the destination</item>
///   <item>Email → all registered Email providers that can handle the destination</item>
/// </list>
/// </para>
/// <para>
/// Singleton lifetime. Uses a 5-minute in-memory cache per (channel, destination prefix).
/// Slice 2 (DB-backed routing) replaces the chain resolution logic; the cache stays.
/// </para>
/// </summary>
public sealed class NotificationProviderRegistry : INotificationProviderRegistry
{
    // For slice 1 the "chain" is just the full provider list filtered by channel + CanHandle.
    // The cache key is (tenantId, channel) — destination filtering (CanHandle) is applied
    // after the 5-min cache to keep cache entries coarse-grained.
    private readonly IReadOnlyList<INotificationProvider> _allProviders;
    private readonly ConcurrentDictionary<(Guid TenantId, NotificationChannel Channel), CachedChain> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initialises the registry with the full ordered list of registered providers.
    /// Slice 1: inject <c>TwilioSmsProvider</c> and <c>SendGridEmailProvider</c> (or stubs).
    /// </summary>
    public NotificationProviderRegistry(IEnumerable<INotificationProvider> providers)
    {
        _allProviders = providers.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<INotificationProvider> ResolveChain(
        Guid tenantId,
        NotificationChannel channel,
        string destination)
    {
        var now = DateTimeOffset.UtcNow;
        var cacheKey = (tenantId, channel);

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
            return FilterByCanHandle(cached.Providers, destination);

        // Build new chain for this (tenant, channel) — no DB in slice 1
        var chain = _allProviders
            .Where(p => p.Channel == channel)
            .ToList();

        _cache[cacheKey] = new CachedChain(chain, now.Add(CacheTtl));
        return FilterByCanHandle(chain, destination);
    }

    private static IReadOnlyList<INotificationProvider> FilterByCanHandle(
        IReadOnlyList<INotificationProvider> chain,
        string destination)
        => chain.Where(p => p.CanHandle(destination)).ToList();

    private sealed record CachedChain(IReadOnlyList<INotificationProvider> Providers, DateTimeOffset ExpiresAt);
}
