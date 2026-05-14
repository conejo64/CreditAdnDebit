using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum InterestSegment
{
    Purchase = 1,
    CashAdvance = 2,
    Penalty = 3
}

public sealed class InterestAccrualRecordEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public DateOnly AccrualDate { get; set; }

    public InterestSegment Segment { get; set; } = InterestSegment.Purchase;

    /// <summary>Base balance used for interest calculation (>=0).</summary>
    public decimal BalanceBase { get; set; }

    /// <summary>APR used (e.g. 0.35).</summary>
    public decimal Apr { get; set; }

    /// <summary>Daily rate = Apr/365.</summary>
    public decimal DailyRate { get; set; }

    /// <summary>Interest amount posted for the day.</summary>
    public decimal InterestAmount { get; set; }

    public Guid? LedgerEntryId { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
