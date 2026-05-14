using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public sealed class StatementLineEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid StatementId { get; set; }
    public StatementEntity Statement { get; set; } = default!;

    public Guid? LedgerEntryId { get; set; }
    public LedgerEntryEntity? LedgerEntry { get; set; }

    public DateTimeOffset PostedOn { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = default!;
}
