using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;

namespace IsoSwitch.Infrastructure.Messaging;

/// <summary>
/// Audit sink for the binary TCP listener (<c>TcpIso8583Server</c>).
///
/// The raw ISO 8583 frame is never published or persisted: it carries DE2, DE35 and DE52 in clear,
/// and no consumer (the IsoAudit microservice, <c>/api/iso/logs</c>) parses the hex. Each frame is
/// reduced to its length and SHA-256 digest, which still lets an operator correlate a frame
/// captured on the wire with its audit row without the switch retaining card data. Field-level
/// views (<see cref="LogAsync"/>) go through <see cref="CardDataMasking"/>.
/// </summary>
public sealed class BinaryIsoAuditService : IIsoAuditService
{
    private const string EventName = "iso.audit.v1";

    private readonly IsoSwitchDbContext _db;
    private readonly ISwitchEventPublisher _publisher;
    private readonly bool _writeToDb;

    public BinaryIsoAuditService(IsoSwitchDbContext db, ISwitchEventPublisher publisher, IConfiguration cfg)
    {
        _db = db;
        _publisher = publisher;
        _writeToDb = cfg.GetValue<bool?>("Audit:WriteToDb") ?? false; // v54 default: false (audit microservice persists)
    }

    public Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct)
    {
        var stan = msg.Fields.TryGetValue(11, out var f11) ? f11 : "000000";
        var rrn = msg.Fields.TryGetValue(37, out var f37) ? f37 : "000000000000";

        var fields = CardDataMasking.MaskFields(msg.Fields)
            .OrderBy(k => k.Key)
            .ToDictionary(k => k.Key.ToString(), v => v.Value);

        var details = new Dictionary<string, object?>
        {
            ["kind"] = "fields",
            ["fields"] = fields
        };

        return PublishAndPersistAsync(traceId, direction, msg.Mti, stan, rrn, details, ct);
    }

    /// <summary>
    /// Audits one raw frame by metadata only: <c>frameLength</c>, <c>frameSha256</c> and
    /// <c>tpduLength</c>. The bytes themselves are not retained. <paramref name="extra"/> is written
    /// as given, so callers must pass only already-safe values (token PAN, masked PAN, amounts).
    /// </summary>
    public Task LogFrameAsync(
        string traceId,
        string direction,
        string mti,
        string stan,
        string rrn,
        byte[] frame,
        byte[]? tpdu,
        object? extra,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var details = new Dictionary<string, object?>
        {
            ["kind"] = "frame",
            ["frameLength"] = frame.Length,
            ["frameSha256"] = Convert.ToHexString(SHA256.HashData(frame)),
            ["tpduLength"] = tpdu?.Length,
            ["extra"] = extra
        };

        return PublishAndPersistAsync(traceId, direction, mti, stan, rrn, details, ct);
    }

    private async Task PublishAndPersistAsync(
        string traceId,
        string direction,
        string mti,
        string stan,
        string rrn,
        Dictionary<string, object?> details,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["traceId"] = traceId,
            ["direction"] = direction,
            ["mti"] = mti,
            ["stan"] = stan,
            ["rrn"] = rrn
        };
        foreach (var (key, value) in details) payload[key] = value;

        var envelope = new
        {
            eventName = EventName,
            eventId = Guid.NewGuid().ToString("N"),
            occurredOn = DateTimeOffset.UtcNow,
            payload
        };

        // Publish to Kafka (IsoAudit microservice consumes)
        await _publisher.PublishAuditAsync(traceId, envelope, ct);

        if (_writeToDb)
        {
            var row = new Dictionary<string, object?> { ["mti"] = mti, ["stan"] = stan, ["rrn"] = rrn };
            foreach (var (key, value) in details) row[key] = value;

            _db.IsoMessageLogs.Add(new IsoMessageLogEntity
            {
                TraceId = traceId,
                Direction = direction,
                Mti = mti,
                FieldsJson = JsonSerializer.Serialize(row)
            });

            await _db.SaveChangesAsync(ct);
        }
    }
}
