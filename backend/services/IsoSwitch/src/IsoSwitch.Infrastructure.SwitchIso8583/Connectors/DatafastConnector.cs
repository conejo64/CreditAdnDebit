using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

public sealed class DatafastConnector : IAcquirerConnector
{
    public string ConnectorId => "DATAFAST";

    private readonly TcpIsoClient _client;
    private readonly PackagerRegistry _packagers;
    private readonly ILogger<DatafastConnector> _logger;

    public DatafastConnector(TcpIsoClient client, PackagerRegistry packagers, ILogger<DatafastConnector> logger)
    {
        _client = client;
        _packagers = packagers;
        _logger = logger;
    }

    public async Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Sending Datafast Authorization Request");
            return await _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Datafast Authorization failed due to network or timeout error");
            throw;
        }
    }

    public async Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Sending Datafast Reversal Request");
            return await _client.SendAsync(request, _packagers.Get(ConnectorId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Datafast Reversal failed due to network or timeout error");
            throw;
        }
    }
}
