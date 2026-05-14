using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Loyalty;

public enum RewardCatalogItemStatus
{
    Active = 1,
    Inactive = 2
}

public sealed class RewardCatalogItemEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(40)]
    public string Code { get; set; } = default!;

    [MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(300)]
    public string Description { get; set; } = default!;

    public decimal PointsCost { get; set; }

    public decimal CashbackCost { get; set; }

    public RewardCatalogItemStatus Status { get; set; } = RewardCatalogItemStatus.Active;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}
