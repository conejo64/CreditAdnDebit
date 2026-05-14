using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum StatementStatus
{
    Open = 1,
    Closed = 2
}

public sealed class StatementEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public DateTime CycleStart { get; set; }
    public DateTime CycleEnd { get; set; }

    public DateTime StatementDate { get; set; }
    public DateTime DueDate { get; set; }

    public decimal PreviousBalance { get; set; }
    public decimal Purchases { get; set; }
    public decimal Payments { get; set; } // negative
    public decimal Fees { get; set; }
    public decimal Interest { get; set; }
    public decimal InterestAccrued { get; set; }
    public decimal StatementBalance { get; set; }

    public decimal NewBalance { get; set; }
    public decimal MinimumPayment { get; set; }
    public decimal TotalPaymentDue { get; set; }


    // v32 - interest & delinquency
    public decimal AverageDailyBalance { get; set; }
    public decimal InterestApr { get; set; }
    public int InterestDays { get; set; }

    // v37 - payment allocation buckets
    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public decimal FeesDue { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal PaidToPrincipal { get; set; }
    public decimal PaidToInterest { get; set; }
    public decimal PaidToFees { get; set; }
    public DateTimeOffset? LateFeeAppliedOn { get; set; }
    public decimal LateFeeAmount { get; set; }

    public StatementStatus Status { get; set; } = StatementStatus.Open;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<StatementLineEntity> Lines { get; set; } = new();
}
