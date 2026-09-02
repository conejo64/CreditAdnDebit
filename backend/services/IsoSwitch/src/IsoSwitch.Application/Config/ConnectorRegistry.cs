using BuildingBlocks.Commercial;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Application.Config;

public sealed class ConnectorRegistry
{
    private const string SimulatorConnectorId = "SIMULATOR";

    private readonly Dictionary<string, IAcquirerConnector> _map;
    private readonly bool _forceSimulator;
    private readonly CommercialOptions _commercialOptions;

    public ConnectorRegistry(IEnumerable<IAcquirerConnector> connectors, IConfiguration configuration)
        : this(
            connectors,
            configuration,
            Options.Create(new CommercialOptions
            {
                Mode = CommercialMode.Demo,
                EnableDemoSurfaces = true,
                ClaimRegisterVersion = "legacy-demo"
            }))
    {
    }

    public ConnectorRegistry(
        IEnumerable<IAcquirerConnector> connectors,
        IConfiguration configuration,
        IOptions<CommercialOptions> commercialOptions)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commercialOptions);

        _map = connectors.ToDictionary(c => c.ConnectorId, StringComparer.OrdinalIgnoreCase);
        _commercialOptions = commercialOptions.Value;

        var forceVal = configuration["Iso:ForceSimulator"];
        _forceSimulator = bool.TryParse(forceVal, out var b) && b;

        if (_commercialOptions.IsCommercialMode && _forceSimulator)
        {
            throw new InvalidOperationException("Iso:ForceSimulator cannot be enabled in commercial mode.");
        }
    }

    public IAcquirerConnector Get(string connectorId)
    {
        if (_forceSimulator && _map.TryGetValue(SimulatorConnectorId, out var forcedSim))
        {
            return forcedSim;
        }

        if (_map.TryGetValue(connectorId, out var c))
        {
            return c;
        }

        if (!_commercialOptions.IsCommercialMode && _map.TryGetValue(SimulatorConnectorId, out var sim))
        {
            return sim;
        }

        throw new InvalidOperationException($"No connector registered for '{connectorId}'");
    }
}
