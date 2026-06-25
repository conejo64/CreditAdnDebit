using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.IsoAudit;
using System.Text.Json;

namespace IsoSwitch.Infrastructure.Messaging;

public sealed class IsoAuditService : IIsoAuditService
{
    private readonly IsoSwitchDbContext _db;

    public IsoAuditService(IsoSwitchDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct)
    {
        var dict = new Dictionary<int, string?>(msg.Fields);

        // Mask sensitive fields: 2(PAN), 52(PIN), 55(EMV), 64/128(MAC)
        if (dict.ContainsKey(2)) dict[2] = "***";
        if (dict.ContainsKey(52)) dict[52] = "***";
        if (dict.ContainsKey(55)) dict[55] = "***";
        if (dict.ContainsKey(64)) dict[64] = "***";
        if (dict.ContainsKey(128)) dict[128] = "***";

        var json = JsonSerializer.Serialize(new
        {
            mti = msg.Mti,
            fields = dict.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), v => v.Value)
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