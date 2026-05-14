using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.Persistence.Routing;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Routing;

public sealed class RoutingEngineV2 : IRoutingEngineV2
{
    private readonly IsoSwitchDbContext _db;

    public RoutingEngineV2(IsoSwitchDbContext db)
    {
        _db = db;
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

        // if country not provided, infer from BIN cache if available
        if (countryCode is null && !string.IsNullOrWhiteSpace(binInfo?.CountryCode))
            countryCode = binInfo.CountryCode.Trim().ToUpperInvariant();

        // prefer V2 rules
        var q = _db.RoutingRulesV2.Where(r => r.Enabled && bin >= r.BinStart && bin <= r.BinEnd);

        // Optional dimensions: if rule has value, it must match; if rule is null, wildcard
        if (countryCode is not null) q = q.Where(r => r.CountryCode == null || r.CountryCode == countryCode);
        if (network is not null) q = q.Where(r => r.Network == null || r.Network == network);

        q = q.Where(r => r.TxType == null || r.TxType == txType);

        var rule = await q.OrderBy(r => r.Priority).FirstOrDefaultAsync(ct);

        if (rule is null)
        {
            // fallback to legacy rules
            var legacy = await _db.RoutingRulesCache
                .Where(r => r.Enabled && bin >= r.BinStart && bin <= r.BinEnd)
                .OrderBy(r => r.Priority)
                .FirstOrDefaultAsync(ct);

            var connectorFallback = legacy?.ConnectorId ?? "SIMULATOR";
            return new RoutingDecision(connectorFallback, "LEGACY", legacy?.Id, binInfo, null);
        }

        return new RoutingDecision(rule.ConnectorId, "V2", rule.Id, binInfo, rule);
    }
}

public sealed record RoutingDecision(
    string ConnectorId,
    string Mode,
    Guid? MatchedRuleId,
    BinRangeCacheEntity? BinInfo,
    RoutingRuleV2Entity? RuleV2);