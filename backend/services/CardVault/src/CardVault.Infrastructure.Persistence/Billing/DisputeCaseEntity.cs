using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum DisputeStatus
{
    Open = 1,
    Representment = 2,
    PreArbitration = 3,
    Arbitration = 4,
    Won = 5,
    Lost = 6,
    Closed = 7
}

/// <summary>
/// Simplified dispute/chargeback case.
/// </summary>
public sealed class DisputeCaseEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid? OriginalTxnJournalId { get; set; }

    [MaxLength(32)]
    public string Network { get; set; } = "Visa";

    [MaxLength(8)]
    public string Stan { get; set; } = "000000";

    [MaxLength(32)]
    public string Rrn { get; set; } = "";

    [MaxLength(8)]
    public string ReasonCode { get; set; } = "0000";

    public decimal OriginalAmount { get; set; }

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;

    public Guid? ProvisionalCreditLedgerEntryId { get; set; }

    public DateTimeOffset OpenedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ResolvedOn { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }
}
