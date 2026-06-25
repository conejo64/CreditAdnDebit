using CardVault.Domain;
using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

/// <summary>
/// Signed amounts:
/// - Charges (Purchase/Fee/Interest) are positive
/// - Payments are negative
/// </summary>
public sealed class LedgerEntryEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public LedgerEntryType Type { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = default!;

    public DateTimeOffset PostedOn { get; set; } = DateTimeOffset.UtcNow;

    public Guid? StatementId { get; set; }
    public StatementEntity? Statement { get; set; }
}
