using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Settlement;

public sealed class SettlementItemEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }
    public SettlementBatchEntity Batch { get; set; } = default!;

    public Guid LedgerEntryId { get; set; }

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(64)]
    public string NetworkRef { get; set; } = default!; // e.g. RRN/Trace/ARN (simplified)

    public DateTimeOffset PostedOn { get; set; }
}
