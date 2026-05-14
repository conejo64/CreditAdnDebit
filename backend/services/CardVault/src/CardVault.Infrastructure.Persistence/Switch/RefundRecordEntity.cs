using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Switch;

/// <summary>
/// Tracks partial refunds against an original purchase (by Network+RRN).
/// </summary>
public sealed class RefundRecordEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    [MaxLength(16)]
    public string Network { get; set; } = default!;

    [MaxLength(12)]
    public string Rrn { get; set; } = default!;

    public decimal Amount { get; set; }

    [MaxLength(6)]
    public string Stan { get; set; } = default!;

    public Guid? LedgerEntryId { get; set; }

    public DateTimeOffset PostedOn { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
