using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using System.Threading;
using System.Threading.Tasks;

namespace IsoSwitch.Api;

public interface IIsoAuditService
{
    Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct);
}
