using System.Threading;
using System.Threading.Tasks;

namespace IsoSwitch.Application.Ports;

public interface ISwitchEventPublisher
{
    Task PublishTxAsync(string key, object payload, CancellationToken ct);
    Task PublishIsoAsync(string key, object payload, CancellationToken ct);
    Task PublishAuditAsync(string key, object payload, CancellationToken ct);
}
