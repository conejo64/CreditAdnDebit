using System.Collections.Concurrent;
using System.Text.Json;
using IsoSwitch.Api;
using Microsoft.EntityFrameworkCore;
using IsoSwitch.Infrastructure.Persistence;

namespace IsoSwitch.Api.Tcp;

public static class PanMapStore
{
    // v52: map by tokenPan (TPAN_...)
    public static readonly ConcurrentDictionary<string, Guid> Map = new();

    public static void MapToken(string tokenPan, Guid accountId) => Map[tokenPan] = accountId;
    public static bool TryGetAccount(string tokenPan, out Guid accountId) => Map.TryGetValue(tokenPan, out accountId);

    public static async Task InitializeFromDbAsync(CatalogAuditPersistence audit, CancellationToken ct)
    {
        List<string> events;
        try
        {
            events = await audit.Query("tokenpan.mapped")
                .OrderBy(x => x.OccurredOn)
                .Select(x => x.PayloadJson)
                .ToListAsync(ct);
        }
        catch
        {
            return;
        }

        foreach (var json in events)
        {
            try
            {
                var r = JsonSerializer.Deserialize<TokenPanMap>(json);
                if (r is null) continue;
                if (Guid.TryParse(r.AccountId, out var id))
                    MapToken(r.TokenPan, id);
            }
            catch { }
        }
    }

    public sealed record TokenPanMap(string TokenPan, string AccountId);
}
