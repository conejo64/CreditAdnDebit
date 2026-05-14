using IsoSwitch.Api.Services;
using IsoSwitch.Api.Security;
using IsoSwitch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var auditRead = app.MapGroup("/api")
            .RequireAuthorization(IsoSwitchAuthorizationPolicies.ViewAudit);

        // v28 - Audit read (PCI-safe)
        auditRead.MapGet("/audit/latest", async (AuditService audit, int take, CancellationToken ct) =>
        {
            take = take <= 0 ? 50 : Math.Min(take, 500);
            var list = await audit.LatestAsync(take, ct);
            return Results.Ok(list);
        }).WithOpenApi();

        auditRead.MapGet("/iso/audit/logs", async (int take, IsoSwitchDbContext db) =>
        {
            take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);
            var logs = await db.IsoMessageLogs.OrderByDescending(x => x.CreatedOn).Take(take).Select(x => new { x.TraceId, x.Direction, x.Mti, x.CreatedOn, x.FieldsJson }).ToListAsync();
            return Results.Ok(logs);
        }).WithOpenApi();
    }
}
