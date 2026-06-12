using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

/// <summary>
/// Connector for a real external ISO8583 gateway over TCP/TLS.
/// Uses TcpIsoClientOptions from configuration.
/// </summary>
public sealed class TcpGatewayConnector : IAcquirerConnector
{
    public string ConnectorId => "TCP_GATEWAY";

    private readonly TcpIsoClient _client;
    private readonly PackagerRegistry _packagers;

    public TcpGatewayConnector(IConfiguration cfg, PackagerRegistry packagers, ILogger<TcpIsoClient> logger)
    {
        _packagers = packagers;

        var opt = new TcpIsoClientOptions();
        cfg.GetSection("Connectors:TcpGateway").Bind(opt);

        _client = new TcpIsoClient(opt, logger);
    }

    public Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct)
        => _client.SendAsync(request, _packagers.Get(ConnectorId), ct);

    public Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct)
        => _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
}
