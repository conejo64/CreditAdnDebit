using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum LedgerEntryType
{
    Purchase = 1,
    Payment = 2,
    Fee = 3,
    Interest = 4,
    Adjustment = 5,
    Refund = 6,
    Reversal = 7,
    Chargeback = 8,
    AuthorizationHold = 9,
    Clearing = 10
}

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
