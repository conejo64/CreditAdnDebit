using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Accounting;

public sealed class AccountingMappingEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(50)]
    public string EventType { get; set; } = default!;

    [MaxLength(30)]
    public string ProductCode { get; set; } = "*";

    [MaxLength(30)]
    public string DebitAccountCode { get; set; } = default!;

    [MaxLength(30)]
    public string CreditAccountCode { get; set; } = default!;

    public DateOnly EffectiveDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
