using System.Threading;
using System.Threading.Tasks;
using IsoSwitch.Domain;

namespace IsoSwitch.Application.Ports;

public interface IIsoAuditService
{
    Task LogAsync(string traceId, string direction, IsoMessage msg, CancellationToken ct);
}
