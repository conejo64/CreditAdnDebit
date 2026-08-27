using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

public sealed class BanredConnector : IAcquirerConnector
{
    public string ConnectorId => "BANRED";

    private readonly TcpIsoClient _client;
    private readonly PackagerRegistry _packagers;
    private readonly ILogger<BanredConnector> _logger;

    public BanredConnector(TcpIsoClient client, PackagerRegistry packagers, ILogger<BanredConnector> logger)
    {
        _client = client;
        _packagers = packagers;
        _logger = logger;
    }

    public async Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Sending Banred Authorization Request");
            return await _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Banred Authorization failed due to network or timeout error");
            throw;
        }
    }

    public async Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Sending Banred Reversal Request");
            return await _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Banred Reversal failed due to network or timeout error");
            throw;
        }
    }
}
