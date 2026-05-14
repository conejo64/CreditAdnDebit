using IsoSwitch.Api.Services;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using MediatR;

namespace IsoSwitch.Api.Features.Transactions.Commands.NetworkManagement;

public class NetworkCommandHandler : IRequestHandler<NetworkCommand, NetworkResult>
{
    private readonly ConnectorRegistry _registry;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;

    public NetworkCommandHandler(
        ConnectorRegistry registry,
        ISwitchEventPublisher publisher,
        IIsoAuditService audit)
    {
        _registry = registry;
        _publisher = publisher;
        _audit = audit;
    }

    public async Task<NetworkResult> Handle(NetworkCommand request, CancellationToken ct)
    {
        var iso = new IsoMessage { Mti = "0800" };
        
        // Field 70: 301=Echo Test/Sign On, 302=Sign Off (Based on original logic)
        var networkCode = request.Operation switch
        {
            NetworkOperation.Ping => "301",
            NetworkOperation.SignOn => "301",
            NetworkOperation.SignOff => "302",
            _ => throw new ArgumentOutOfRangeException(nameof(request.Operation))
        };
        
        iso.Set(70, networkCode);

        // Audit & Publish (Sent)
        await _audit.LogAsync(request.TraceId, "OUT", iso, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new 
        { 
            type = "sw.iso.sent", 
            traceId = request.TraceId, 
            mti = iso.Mti, 
            connectorId = request.ConnectorId, 
            at = DateTimeOffset.UtcNow 
        }, ct);

        // Send to Connector
        var connector = _registry.Get(request.ConnectorId);
        
        // Network messages use AuthorizeAsync in this system's connector interface
        var resp = await connector.AuthorizeAsync(iso, ct);

        // Audit & Publish (Received)
        await _audit.LogAsync(request.TraceId, "IN", resp, ct);
        await _publisher.PublishIsoAsync(request.TraceId, new 
        { 
            type = "sw.iso.received", 
            traceId = request.TraceId, 
            mti = resp.Mti, 
            connectorId = request.ConnectorId, 
            at = DateTimeOffset.UtcNow 
        }, ct);

        var rc = resp.Fields.TryGetValue(39, out var code) ? code : "XX";
        var status = rc == "00" ? "SUCCESS" : "FAILED";

        return new NetworkResult(
            request.TraceId,
            resp.Mti,
            rc,
            status
        );
    }
}
