using IsoSwitch.Infrastructure.SwitchIso8583.Iso;

namespace IsoSwitch.Infrastructure.SwitchIso8583.Connectors;

public interface IAcquirerConnector
{
    string ConnectorId { get; }
    Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct);
    Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct);
}