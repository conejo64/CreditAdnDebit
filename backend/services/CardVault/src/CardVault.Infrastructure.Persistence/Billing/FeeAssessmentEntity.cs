using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum FeeType
{
    Overlimit = 1,
    Annual = 2,
    CashAdvance = 3
}

/// <summary>
/// Records fee assessments to enforce idempotency and for audit.
/// </summary>
public sealed class FeeAssessmentEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public FeeType FeeType { get; set; }

    public DateOnly BusinessDate { get; set; }

    public decimal Amount { get; set; }

    public Guid? LedgerEntryId { get; set; }

    [MaxLength(256)]
    public string? Notes { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
