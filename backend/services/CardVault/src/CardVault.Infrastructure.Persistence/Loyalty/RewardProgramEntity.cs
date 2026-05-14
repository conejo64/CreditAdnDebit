using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Loyalty;

public sealed class RewardProgramEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ProductCode { get; set; } = default!;

    [MaxLength(100)]
    public string ProgramName { get; set; } = default!;

    public decimal CashbackRate { get; set; }

    public decimal PointsPerCurrencyUnit { get; set; }

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    public bool IsActive { get; set; } = true;

    public DateOnly EffectiveDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}
