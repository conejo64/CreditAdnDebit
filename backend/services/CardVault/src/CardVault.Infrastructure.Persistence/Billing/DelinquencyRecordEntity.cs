using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum DelinquencyBucket
{
    DaysOneToThirty   = 1,  // 1-30 days
    DaysThirtyOneToSixty = 2, // 31-60 days
    DaysSixtyOneToNinety = 3, // 61-90 days
    OverNinety        = 4   // >90 days
}

public enum DelinquencyRecordStatus
{
    Active   = 1,
    Resolved = 2
}

/// <summary>
/// Tracks a delinquency cycle for a credit account.
/// One record is created per statement that triggers mora, and is resolved
/// when the overdue amount is fully covered by payments.
/// </summary>
public sealed class DelinquencyRecordEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the credit account in mora.</summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// FK to the Statement that originated the delinquency.
    /// Allows tracking which billing cycle was missed.
    /// </summary>
    public Guid StatementId { get; set; }

    /// <summary>Remaining unpaid amount: MinimumPayment - TotalPayments received against the statement.</summary>
    public decimal OverdueAmount { get; set; }

    /// <summary>Number of calendar days since the statement DueDate.</summary>
    public int DaysInArrears { get; set; }

    /// <summary>Aging bucket computed from DaysInArrears.</summary>
    public DelinquencyBucket Bucket { get; set; }

    /// <summary>Active = account still in mora; Resolved = customer has caught up.</summary>
    public DelinquencyRecordStatus Status { get; set; } = DelinquencyRecordStatus.Active;

    public DateTimeOffset CreatedOn  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedOn  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedOn { get; set; }
}
