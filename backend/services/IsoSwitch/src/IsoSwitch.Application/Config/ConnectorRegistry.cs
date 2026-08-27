using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using Microsoft.Extensions.Configuration;

namespace IsoSwitch.Application.Config;

public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IAcquirerConnector> _map;
    private readonly bool _forceSimulator;

    public ConnectorRegistry(IEnumerable<IAcquirerConnector> connectors, IConfiguration configuration)
    {
        _map = connectors.ToDictionary(c => c.ConnectorId, StringComparer.OrdinalIgnoreCase);
        _forceSimulator = configuration.GetValue<bool>("Iso:ForceSimulator", false);
    }

    public IAcquirerConnector Get(string connectorId)
    {
        if (_forceSimulator && _map.TryGetValue("SIMULATOR", out var forcedSim))
        {
            return forcedSim;
        }

        if (_map.TryGetValue(connectorId, out var c))
            return c;
        if (_map.TryGetValue("SIMULATOR", out var sim))
            return sim;
        throw new InvalidOperationException($"No connector registered for '{connectorId}'");
    }
}
