using BuildingBlocks.Commercial;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.Persistence.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Routing;

public sealed class RoutingEngineV2 : IRoutingEngineV2
{
    private readonly IsoSwitchDbContext _db;
    private readonly CommercialOptions _commercialOptions;

    public RoutingEngineV2(IsoSwitchDbContext db)
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

    public RoutingEngineV2(IsoSwitchDbContext db, IOptions<CommercialOptions> commercialOptions)
    {
        _db = db;
        _commercialOptions = commercialOptions.Value;
    }

    public async Task<RoutingDecision> ResolveAsync(int bin, string? countryCode, string? network, string txType, CancellationToken ct)
    {
        countryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        network = string.IsNullOrWhiteSpace(network) ? null : network.Trim().ToUpperInvariant();
        txType = string.IsNullOrWhiteSpace(txType) ? "AUTH" : txType.Trim().ToUpperInvariant();

        var binInfo = await _db.BinRangesCache
            .Where(b => b.Enabled && bin >= b.BinStart && bin <= b.BinEnd)
            .OrderBy(b => b.BinStart)
            .FirstOrDefaultAsync(ct);

        if (countryCode is null && !string.IsNullOrWhiteSpace(binInfo?.CountryCode))
        {
            countryCode = binInfo.CountryCode.Trim().ToUpperInvariant();
        }

        var q = _db.RoutingRulesV2.Where(r => r.Enabled && bin >= r.BinStart && bin <= r.BinEnd);

        if (countryCode is not null) q = q.Where(r => r.CountryCode == null || r.CountryCode == countryCode);
        if (network is not null) q = q.Where(r => r.Network == null || r.Network == network);

        q = q.Where(r => r.TxType == null || r.TxType == txType);

        var rule = await q.OrderBy(r => r.Priority).FirstOrDefaultAsync(ct);

        if (rule is not null)
        {
            return new RoutingDecision(rule.ConnectorId, "V2", rule.Id, binInfo, rule);
        }

        var legacy = await _db.RoutingRulesCache
            .Where(r => r.Enabled && bin >= r.BinStart && bin <= r.BinEnd)
            .OrderBy(r => r.Priority)
            .FirstOrDefaultAsync(ct);

        if (legacy is not null)
        {
            return new RoutingDecision(legacy.ConnectorId, "LEGACY", legacy.Id, binInfo, null);
        }

        if (_commercialOptions.IsCommercialMode)
        {
            throw new InvalidOperationException($"No commercial mode route resolved for BIN {bin}.");
        }

        return new RoutingDecision("SIMULATOR", "DEMO_FALLBACK", null, binInfo, null);
    }
}

public sealed record RoutingDecision(
    string ConnectorId,
    string Mode,
    Guid? MatchedRuleId,
    BinRangeCacheEntity? BinInfo,
    RoutingRuleV2Entity? RuleV2);
