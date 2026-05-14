using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Settlement;

public enum SettlementNetwork
{
    Visa = 1,
    MasterCard = 2,
    Discover = 3,
    Diners = 4,
    Other = 99
}

public enum SettlementBatchStatus
{
    Open = 1,
    Closed = 2,
    Exported = 3
}

/// <summary>
/// Daily clearing/settlement batch aggregated by network.
/// v34 - simplified model for demo.
/// </summary>
public sealed class SettlementBatchEntity
{
    [Key]
    public Guid Id { get; set; }

    public SettlementNetwork Network { get; set; }

    public DateOnly BusinessDate { get; set; }

    public SettlementBatchStatus Status { get; set; } = SettlementBatchStatus.Open;

    public int TxnCount { get; set; }
    public decimal GrossAmount { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<SettlementItemEntity> Items { get; set; } = new();
}
