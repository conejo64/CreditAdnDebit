using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

/// <summary>
/// Configurable minimum payment policy (simplified v38).
/// Minimum = InterestDue + FeesDue + max(FloorAmount, PrincipalPercent * PrincipalDue), capped by CeilingAmount (if set).
/// If TotalPaymentDue < FloorAmount => Minimum = TotalPaymentDue.
/// </summary>
public sealed class MinimumPaymentPolicyEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = "DEFAULT";

    public bool IsDefault { get; set; } = true;

    /// <summary>Absolute minimum floor (e.g. 10.00)</summary>
    public decimal FloorAmount { get; set; } = 10m;

    /// <summary>Percent applied to principal due (e.g. 0.05 for 5%)</summary>
    public decimal PrincipalPercent { get; set; } = 0.05m;

    /// <summary>If set (>0), caps minimum payment</summary>
    public decimal? CeilingAmount { get; set; }

    public bool IncludeInterest { get; set; } = true;
    public bool IncludeFees { get; set; } = true;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
