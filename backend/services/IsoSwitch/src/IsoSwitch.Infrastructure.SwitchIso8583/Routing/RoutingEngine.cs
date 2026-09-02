using BuildingBlocks.Commercial;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Routing;

public sealed class RoutingEngine
{
    private readonly IsoSwitchDbContext _db;
    private readonly CommercialOptions _commercialOptions;

    public RoutingEngine(IsoSwitchDbContext db)
        : this(
            db,
            Options.Create(new CommercialOptions
            {
                Mode = CommercialMode.Demo,
                EnableDemoSurfaces = true,
                ClaimRegisterVersion = "legacy-demo"
            }))
    {
    }

    public RoutingEngine(IsoSwitchDbContext db, IOptions<CommercialOptions> commercialOptions)
    {
        _db = db;
        _commercialOptions = commercialOptions.Value;
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

        if (rule is not null)
        {
            return (rule.ConnectorId, binInfo);
        }

        if (_commercialOptions.IsCommercialMode)
        {
            throw new InvalidOperationException($"No commercial mode route resolved for BIN {bin}.");
        }

        return ("SIMULATOR", binInfo);
    }
}
