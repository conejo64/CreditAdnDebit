using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Loyalty;

public enum LoyaltyEntryType
{
    Accrual = 1,
    Redemption = 2,
    Reversal = 3
}

public sealed class LoyaltyEntryEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid LoyaltyBalanceId { get; set; }
    public LoyaltyBalanceEntity LoyaltyBalance { get; set; } = default!;

    public Guid AccountId { get; set; }

    public LoyaltyEntryType EntryType { get; set; }

    public decimal CashbackAmount { get; set; }

    public decimal PointsAmount { get; set; }

    [MaxLength(40)]
    public string SourceType { get; set; } = default!;

    [MaxLength(100)]
    public string SourceReference { get; set; } = default!;

    [MaxLength(250)]
    public string Description { get; set; } = default!;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
