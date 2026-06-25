using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

namespace IsoSwitch.Application.Config;

public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IAcquirerConnector> _map;

    public ConnectorRegistry(IEnumerable<IAcquirerConnector> connectors)
    {
        _map = connectors.ToDictionary(c => c.ConnectorId, StringComparer.OrdinalIgnoreCase);
    }

    public IAcquirerConnector Get(string connectorId)
    {
        if (_map.TryGetValue(connectorId, out var c))
            return c;
        if (_map.TryGetValue("SIMULATOR", out var sim))
            return sim;
        throw new InvalidOperationException($"No connector registered for '{connectorId}'");
    }
}
