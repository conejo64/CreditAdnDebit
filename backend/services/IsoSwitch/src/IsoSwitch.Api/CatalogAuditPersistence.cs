using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Audit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsoSwitch.Api;

public sealed class CatalogAuditPersistence
{
    private readonly IsoSwitchDbContext _db;

    public CatalogAuditPersistence(IsoSwitchDbContext db)
    {
        _db = db;
    }

    public async Task AppendEventAsync(string eventType, string correlationId, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            Service = "IsoSwitch",
            EventType = eventType,
            CorrelationId = correlationId,
            TraceId = correlationId,
            PayloadSha256 = sha,
            PayloadJson = json,
            OccurredOn = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    public IQueryable<AuditEventEntity> Query(string eventType) =>
        _db.AuditEvents.Where(x => x.EventType == eventType);
}
