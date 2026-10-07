using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using System.Text.Json;

namespace IsoSwitch.Infrastructure.Messaging;

/// <summary>
/// Persists one <see cref="IsoMessageLogEntity"/> per message direction. Card data is masked through
/// <see cref="CardDataMasking"/> before anything is written; the log is read back by operators via
/// <c>/api/iso/logs/{traceId}</c>, so whatever lands here is effectively displayed.
/// </summary>
public sealed class IsoAuditService : IIsoAuditService
{
    private readonly IsoSwitchDbContext _db;

    public IsoAuditService(IsoSwitchDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct)
    {
        var masked = CardDataMasking.MaskFields(msg.Fields);

        var json = JsonSerializer.Serialize(new
        {
            mti = msg.Mti,
            fields = masked.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), v => v.Value)
        });

        _db.IsoMessageLogs.Add(new IsoMessageLogEntity
        {
            TraceId = traceId,
            Direction = direction,
            Mti = msg.Mti,
            FieldsJson = json
        });

        await _db.SaveChangesAsync(ct);
    }
}
