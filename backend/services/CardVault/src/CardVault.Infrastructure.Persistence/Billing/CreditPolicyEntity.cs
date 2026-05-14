using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

/// <summary>
/// Parametrizable policy per product.
/// </summary>
public sealed class CreditPolicyEntity
{
    [Key]
    [MaxLength(64)]
    public string ProductCode { get; set; } = default!;

    /// <summary>Minimum payment percent over NewBalance (e.g. 0.05 = 5%).</summary>
    public decimal MinPaymentPercent { get; set; } = 0.05m;

    /// <summary>Absolute floor for min payment.</summary>
    public decimal MinPaymentAbsolute { get; set; } = 15m;

    /// <summary>Grace days from statement date to due date.</summary>
    public int GraceDays { get; set; } = 15;

    public int HoldTtlHours { get; set; } = 72; // v43 preauth hold TTL hours

    public decimal FloorLimit { get; set; } = 0m; // v44: minimum amount requiring extra checks
    public bool AllowOverlimit { get; set; } = false; // v44: allow authorization over credit limit
    public decimal OverlimitBufferAmount { get; set; } = 0m; // v71: extra tolerance above available credit

    /// <summary>Legacy APR (kept for backward compatibility).</summary>
    public decimal InterestApr { get; set; } = 0.35m;

    /// <summary>APR for purchases (daily accrual).</summary>
    public decimal PurchaseApr { get; set; } = 0.35m;

    /// <summary>APR for cash advances (placeholder).</summary>
    public decimal CashAdvanceApr { get; set; } = 0.45m;

    /// <summary>APR for penalty (placeholder).</summary>
    public decimal PenaltyApr { get; set; } = 0.55m;

    /// <summary>Purchase grace days (simplified: if prev balance <=0, skip interest first N days of range).</summary>
    public int PurchaseGraceDays { get; set; } = 0;

    /// <summary>Late fee applied if past due (placeholder).</summary>
    public decimal LateFee { get; set; } = 10m;

    // v40 - additional fee configuration
    public decimal OverlimitFee { get; set; } = 15m;
    public bool OverlimitFeeOncePerDay { get; set; } = true;
    public decimal AutoIncreasePercent { get; set; } = 0.10m;
    public int AutoIncreaseMinStatements { get; set; } = 3;
    public decimal AutoIncreaseMinOnTimeRatio { get; set; } = 1.00m;
    public decimal AutoIncreaseMinUtilization { get; set; } = 0.35m;

    public decimal AnnualFee { get; set; } = 0m;

    /// <summary>Fixed cash advance fee (demo).</summary>
    public decimal CashAdvanceFeeFixed { get; set; } = 5m;

    /// <summary>Percent cash advance fee (0.03 => 3%).</summary>
    public decimal CashAdvanceFeePercent { get; set; } = 0.03m;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}
