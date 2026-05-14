using System.Threading;
using System.Threading.Tasks;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Routing;

public interface IRoutingEngineV2
{
    Task<RoutingDecision> ResolveAsync(int bin, string? countryCode, string? network, string txType, CancellationToken ct);
}
