using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Switch;

public enum SwitchTxnType
{
    Purchase = 1,
    Authorization = 2,
    Clearing = 3,
    Reversal = 4,
    Refund = 5,
    Chargeback = 6
}

/// <summary>
/// Idempotency journal for switch-originated transactions.
/// Keyed by (Network, Mti, Stan, Rrn).
/// </summary>
public sealed class TxnJournalEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(16)]
    public string Network { get; set; } = default!; // Visa/MasterCard/Discover/Diners

    [MaxLength(4)]
    public string Mti { get; set; } = default!; // e.g. 0100/0200/0400

    [MaxLength(6)]
    public string Stan { get; set; } = default!; // Systems Trace Audit Number

    [MaxLength(12)]
    public string Rrn { get; set; } = default!; // Retrieval Reference Number

    public SwitchTxnType TxnType { get; set; }

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public Guid? LedgerEntryId { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "posted"; // posted/ignored/failed

    public DateTimeOffset PostedOn { get; set; }
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
