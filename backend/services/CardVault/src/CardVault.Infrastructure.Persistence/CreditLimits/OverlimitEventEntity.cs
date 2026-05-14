using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.CreditLimits;

public sealed class OverlimitEventEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid? HoldId { get; set; }

    public decimal ApprovedAmount { get; set; }

    public decimal AvailableCreditBefore { get; set; }

    public decimal OverlimitAmount { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
