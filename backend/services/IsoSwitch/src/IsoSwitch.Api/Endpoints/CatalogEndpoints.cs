using IsoSwitch.Api.Routing;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Catalog;
using IsoSwitch.Infrastructure.Persistence.Routing;
using IsoSwitch.Api.Security;
using Microsoft.EntityFrameworkCore;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;

namespace IsoSwitch.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var routingAdmin = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ManageSwitchRoutes);

        routingAdmin.MapGet("/routing/cache", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var rules = await db.RoutingRulesCache.OrderBy(x => x.Priority).ToListAsync(ct);
            return Results.Ok(rules);
        });

        routingAdmin.MapGet("/catalog/cache/bins", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var list = await db.BinRangesCache.OrderBy(x => x.BinStart).ToListAsync(ct);
            return Results.Ok(list);
        });

        routingAdmin.MapGet("/catalog/cache/countries", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var list = await db.CountriesCache.OrderBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(list);
        });

        routingAdmin.MapGet("/catalog/cache/card-products", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var list = await db.CardProductsCache.OrderBy(x => x.Brand).ThenBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(list);
        });

        routingAdmin.MapPost("/routing/decision", async (IRoutingEngineV2 router, RoutingDecisionRequest req, CancellationToken ct) =>
        {
            var decision = await router.ResolveAsync(req.Bin, req.CountryCode, req.Network, req.TxType ?? "AUTH", ct);
            return Results.Ok(new { connectorId = decision.ConnectorId, mode = decision.Mode, matchedRuleId = decision.MatchedRuleId, binInfo = decision.BinInfo, ruleV2 = decision.RuleV2 });
        }).WithOpenApi();

        routingAdmin.MapPost("/catalog/currencies", async (IsoSwitchDbContext db, CurrencyCacheEntity item, CancellationToken ct) =>
        {
            item.Code = item.Code.Trim().ToUpperInvariant();
            item.UpdatedOn = DateTimeOffset.UtcNow;
            db.CurrenciesCache.Update(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        }).WithOpenApi();

        routingAdmin.MapPost("/catalog/networks", async (IsoSwitchDbContext db, NetworkCacheEntity item, CancellationToken ct) =>
        {
            item.Code = item.Code.Trim().ToUpperInvariant();
            item.UpdatedOn = DateTimeOffset.UtcNow;
            db.NetworksCache.Update(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        }).WithOpenApi();

        routingAdmin.MapPost("/catalog/participants", async (IsoSwitchDbContext db, ParticipantCacheEntity item, CancellationToken ct) =>
        {
            item.ParticipantId = item.ParticipantId.Trim().ToUpperInvariant();
            item.Type = (item.Type ?? "ISSUER").Trim().ToUpperInvariant();
            item.CountryCode = string.IsNullOrWhiteSpace(item.CountryCode) ? null : item.CountryCode.Trim().ToUpperInvariant();
            item.UpdatedOn = DateTimeOffset.UtcNow;
            db.ParticipantsCache.Update(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        }).WithOpenApi();

        routingAdmin.MapPost("/routing/rules/v2", async (IsoSwitchDbContext db, RoutingRuleV2Entity item, CancellationToken ct) =>
        {
            item.Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
            item.CountryCode = string.IsNullOrWhiteSpace(item.CountryCode) ? null : item.CountryCode.Trim().ToUpperInvariant();
            item.Network = string.IsNullOrWhiteSpace(item.Network) ? null : item.Network.Trim().ToUpperInvariant();
            item.TxType = string.IsNullOrWhiteSpace(item.TxType) ? null : item.TxType.Trim().ToUpperInvariant();
            item.ConnectorId = item.ConnectorId.Trim();
            item.UpdatedOn = DateTimeOffset.UtcNow;
            db.RoutingRulesV2.Update(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        }).WithOpenApi();

        routingAdmin.MapDelete("/routing/rules/v2/{id:guid}", async (IsoSwitchDbContext db, Guid id, CancellationToken ct) =>
        {
            var e = await db.RoutingRulesV2.FindAsync(new object[] { id }, ct);
            if (e is null) return Results.NotFound();
            db.RoutingRulesV2.Remove(e);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithOpenApi();

        routingAdmin.MapGet("/routing/rules/v2", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var list = await db.RoutingRulesV2.OrderBy(x => x.Priority).ToListAsync(ct);
            return Results.Ok(list);
        }).WithOpenApi();

        routingAdmin.MapGet("/catalog/currencies", async (IsoSwitchDbContext db, CancellationToken ct) => Results.Ok(await db.CurrenciesCache.OrderBy(x => x.Code).ToListAsync(ct))).WithOpenApi();
        routingAdmin.MapGet("/catalog/networks", async (IsoSwitchDbContext db, CancellationToken ct) => Results.Ok(await db.NetworksCache.OrderBy(x => x.Code).ToListAsync(ct))).WithOpenApi();
        routingAdmin.MapGet("/catalog/participants", async (IsoSwitchDbContext db, CancellationToken ct) => Results.Ok(await db.ParticipantsCache.OrderBy(x => x.ParticipantId).ToListAsync(ct))).WithOpenApi();
        
        routingAdmin.MapGet("/catalog/bin-routes", () =>
        {
            return Results.Ok(BinRoutingStore.Routes.Values.OrderBy(x => x.Bin6).ToList());
        }).WithOpenApi();

        routingAdmin.MapPost("/catalog/bin-routes", async (BinRoutingStore.BinRoute route, CatalogAuditPersistence audit) =>
        {
            if (string.IsNullOrWhiteSpace(route.Bin6) || route.Bin6.Length != 6)
                return Results.BadRequest("Bin6 must be 6 digits");
            BinRoutingStore.AddOrUpdate(route);
            await audit.AppendEventAsync("catalog.binroute.upserted", $"bin:{route.Bin6}", route, CancellationToken.None);
            return Results.Created($"/api/catalog/bin-routes/{route.Bin6}", route);
        }).WithOpenApi();
    }
}
