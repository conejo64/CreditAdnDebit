using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using System.Text.Json;

namespace IsoSwitch.Api;

public sealed class BinaryIsoAuditService : IIsoAuditService
{
    private readonly IsoSwitchDbContext _db;
    private readonly ISwitchEventPublisher _publisher;
    private readonly bool _writeToDb;

    public BinaryIsoAuditService(IsoSwitchDbContext db, ISwitchEventPublisher publisher, IConfiguration cfg)
    {
        _db = db;
        _publisher = publisher;
        _writeToDb = cfg.GetValue<bool?>("Audit:WriteToDb") ?? false; // v54 default: false (audit microservice persists)
    }

    public async Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct)
    {
        var stan = msg.Fields.TryGetValue(11, out var f11) ? f11 : "000000";
        var rrn = msg.Fields.TryGetValue(37, out var f37) ? f37 : "000000000000";
        
        await LogBinaryAsync(
            traceId, 
            direction, 
            msg.Mti, 
            stan, 
            rrn, 
            "ISO-JSON:" + JsonSerializer.Serialize(msg.Fields), 
            null, 
            null, 
            ct);
    }

    public async Task LogBinaryAsync(
        string traceId,
        string direction,
        string mti,
        string stan,
        string rrn,
        string payloadHex,
        string? tpduHex,
        object? extra,
        CancellationToken ct)
    {
        var envelope = new
        {
            eventName = "iso.audit.v1",
            eventId = Guid.NewGuid().ToString("N"),
            occurredOn = DateTimeOffset.UtcNow,
            payload = new
            {
                traceId,
                direction,
                mti,
                stan,
                rrn,
                payloadHex,
                tpduHex,
                extra
            }
        };

        // Publish to Kafka (IsoAudit microservice consumes)
        await _publisher.PublishAuditAsync(traceId, envelope, ct);

        if (_writeToDb)
        {
            var json = JsonSerializer.Serialize(new
            {
                kind = "binary",
                mti,
                stan,
                rrn,
                payloadHex,
                tpduHex,
                extra
            });

            _db.IsoMessageLogs.Add(new IsoMessageLogEntity
            {
                TraceId = traceId,
                Direction = direction,
                Mti = mti,
                FieldsJson = json
            });

            await _db.SaveChangesAsync(ct);
        }
    }
}
