using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using IsoSwitch.Api;
using IsoSwitch.Infrastructure.Persistence;

namespace IsoSwitch.Api.Routing;

public static class BinRoutingStore
{
    public sealed record BinRoute(string Bin6, string Network, string Currency);

    public static readonly ConcurrentDictionary<string, BinRoute> Routes = new();

    public static void SeedDefaults()
    {
        AddOrUpdate(new BinRoute("411111", "VISA", "840"));
        AddOrUpdate(new BinRoute("550000", "MASTERCARD", "840"));
        AddOrUpdate(new BinRoute("601100", "DISCOVER", "840"));
        AddOrUpdate(new BinRoute("360000", "DINERS", "840"));
    }

    public static async Task InitializeFromDbAsync(CatalogAuditPersistence audit, CancellationToken ct)
    {
        List<string> events;
        try
        {
            events = await audit.Query("catalog.binroute.upserted")
                .OrderBy(x => x.OccurredOn)
                .Select(x => x.PayloadJson)
                .ToListAsync(ct);
        }
        catch
        {
            SeedDefaults();
            return;
        }

        if (events.Count == 0)
        {
            SeedDefaults();
            // persist defaults
            foreach (var r in Routes.Values)
            {
                await audit.AppendEventAsync("catalog.binroute.upserted", $"bin:{r.Bin6}", r, ct);
            }
            return;
        }

        foreach (var json in events)
        {
            try
            {
                var r = JsonSerializer.Deserialize<BinRoute>(json);
                if (r is null) continue;
                AddOrUpdate(r);
            }
            catch { }
        }
    }

    public static void AddOrUpdate(BinRoute route) => Routes[route.Bin6] = route;

    public static bool TryResolve(string pan, out BinRoute? route)
    {
        route = null;
        if (string.IsNullOrWhiteSpace(pan) || pan.Length < 6) return false;
        var bin = pan[..6];
        return Routes.TryGetValue(bin, out route);
    }
}
