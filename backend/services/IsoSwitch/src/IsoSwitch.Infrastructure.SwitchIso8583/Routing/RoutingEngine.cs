using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Routing;

public sealed class RoutingEngine
{
    private readonly IsoSwitchDbContext _db;

    public RoutingEngine(IsoSwitchDbContext db)
    {
        _db = db;
    }

    public async Task<(string ConnectorId, BinRangeCacheEntity? Bin)> ResolveAsync(int bin, string merchantId, decimal amount, CancellationToken ct)
    {
        var binInfo = await _db.BinRangesCache
            .Where(b => b.Enabled && bin >= b.BinStart && bin <= b.BinEnd)
            .OrderBy(b => b.BinStart)
            .FirstOrDefaultAsync(ct);

        var rule = await _db.RoutingRulesCache
            .Where(r => r.Enabled && bin >= r.BinStart && bin <= r.BinEnd)
            .OrderBy(r => r.Priority)
            .FirstOrDefaultAsync(ct);

        var connectorId = rule?.ConnectorId ?? "SIMULATOR";
        return (connectorId, binInfo);
    }
}