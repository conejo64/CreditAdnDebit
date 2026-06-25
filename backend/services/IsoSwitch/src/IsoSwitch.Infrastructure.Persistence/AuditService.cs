using System.Security.Cryptography;
using System.Text;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Infrastructure.Persistence;

public sealed class AuditService
{
    private readonly IsoSwitchDbContext _db;

    public AuditService(IsoSwitchDbContext db) => _db = db;

    public async Task WriteAsync(string eventType, object payload, string? correlationId, string? traceId, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        var e = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            Service = "IsoSwitch",
            EventType = eventType,
            CorrelationId = correlationId,
            TraceId = traceId,
            OccurredOn = DateTimeOffset.UtcNow,
            PayloadJson = json,
            PayloadSha256 = sha
        };

        _db.AuditEvents.Add(e);
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<AuditEventEntity>> LatestAsync(int take, CancellationToken ct) =>
        _db.AuditEvents.AsNoTracking().OrderByDescending(x => x.OccurredOn).Take(take).ToListAsync(ct);
}
