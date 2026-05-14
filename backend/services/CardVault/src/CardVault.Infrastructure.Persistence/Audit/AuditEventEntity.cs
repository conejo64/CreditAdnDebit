using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Audit;

public sealed class AuditEventEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Service { get; set; } = "CardVault";

    [MaxLength(128)]
    public string EventType { get; set; } = default!;

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    public DateTimeOffset OccurredOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>PCI-safe payload only.</summary>
    public string PayloadJson { get; set; } = default!;

    [MaxLength(64)]
    public string? PayloadSha256 { get; set; }
}
