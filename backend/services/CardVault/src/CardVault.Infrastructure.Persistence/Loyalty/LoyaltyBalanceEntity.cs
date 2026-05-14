using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Loyalty;

public sealed class LoyaltyBalanceEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public decimal CashbackBalance { get; set; }

    public decimal PointsBalance { get; set; }

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<LoyaltyEntryEntity> Entries { get; set; } = new();
}
