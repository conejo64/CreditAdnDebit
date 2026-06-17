namespace CardVault.Application.Ports;

/// <summary>
/// Port for publishing PCI-DSS audit events.
/// Implemented by CardVault.Api.Pci.PciAuditPublisher (stays in Api).
/// </summary>
public interface IPciAuditPublisher
{
    Task PublishAsync(string eventType, string subject, object payload, CancellationToken ct);
}
