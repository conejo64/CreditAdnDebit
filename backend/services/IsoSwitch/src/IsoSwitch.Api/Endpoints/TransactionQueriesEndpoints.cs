using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Api.Endpoints;

public static class TransactionQueriesEndpoints
{
    public static void MapTransactionQueriesEndpoints(this IEndpointRouteBuilder app)
    {
        var monitor = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ViewSwitchMonitor);
        var auditRead = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ViewAudit);

        auditRead.MapGet("/iso/logs/{traceId}", async (string traceId, IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var logs = await db.IsoMessageLogs.Where(x => x.TraceId == traceId).OrderBy(x => x.CreatedOn).ToListAsync(ct);
            return Results.Ok(logs);
        });

        monitor.MapPost("/iso/reconcile", async (IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var list = await db.Transactions.Where(t => t.InDoubt && t.ReversalStatus == "PENDING").OrderBy(t => t.CreatedOn).Take(100).ToListAsync(ct);
            return Results.Ok(list);
        });

        monitor.MapGet("/transactions", async (IsoSwitchDbContext db, string? from, string? to, string? status, string? type, int? take, CancellationToken ct) =>
        {
            var q = db.Transactions.AsNoTracking().AsQueryable();
            if (DateTimeOffset.TryParse(from, out var fromDt))
                q = q.Where(t => t.CreatedOn >= fromDt);
            if (DateTimeOffset.TryParse(to, out var toDt))
                q = q.Where(t => t.CreatedOn <= toDt);
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(t => t.TxType == type);
            var limit = take is null or <= 0 ? 100 : Math.Min(take.Value, 500);
            var data = await q.OrderByDescending(t => t.CreatedOn).Take(limit).Select(t => new { 
                t.TraceId, t.CorrelationId, t.TxType, t.Status, t.Decision, t.ResponseCode, t.ConnectorId, 
                t.CreatedOn, t.UpdatedOn, t.OriginalTraceId, t.ReversalState,
                t.RequestMti, t.Amount12, t.Currency, t.ProcessingCode, t.Stan
            }).ToListAsync(ct);
            return Results.Ok(new { count = data.Count, items = data });
        });

        monitor.MapGet("/transactions/{traceId}", async (string traceId, IsoSwitchDbContext db, CancellationToken ct) =>
        {
            var tx = await db.Transactions.FirstOrDefaultAsync(x => x.TraceId == traceId, ct);
            return tx is null ? Results.NotFound() : Results.Ok(tx);
        });
    }
}
