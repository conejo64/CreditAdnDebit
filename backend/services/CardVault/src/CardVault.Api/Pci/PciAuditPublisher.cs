using BuildingBlocks.Outbox;
using CardVault.Application.Ports;
using System.Text.Json;

namespace CardVault.Api.Pci;

public sealed class PciAuditPublisher : IPciAuditPublisher
{
    private readonly IEventBus _bus;

    public PciAuditPublisher(IEventBus bus)
    {
        _bus = bus;
    }

    public Task PublishAsync(string eventType, string subject, object payload, CancellationToken ct)
    {
        var msg = JsonSerializer.Serialize(new
        {
            eventType,
            subject,
            at = DateTimeOffset.UtcNow,
            payload
        });

        // PCI audit stream (no PAN, no ciphertext)
        return _bus.PublishAsync("sw.audit.pci", subject, msg, ct);
    }
}