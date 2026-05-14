using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

public sealed class SimulatorConnector : IAcquirerConnector
{
    public string ConnectorId => "SIMULATOR";

    private readonly TcpIsoClient _client;
    private readonly PackagerRegistry _packagers;

    public SimulatorConnector(TcpIsoClient client, PackagerRegistry packagers)
    {
        _client = client;
        _packagers = packagers;
    }

    public Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct)
        => _client.SendAsync(request, _packagers.Get(ConnectorId), ct);

    public Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct)
        => _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
}