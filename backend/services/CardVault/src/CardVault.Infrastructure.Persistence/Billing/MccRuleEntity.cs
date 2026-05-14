using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

/// <summary>
/// Simple MCC rules (blocklist/limit overrides).
/// In production: rules can be per product/customer, time windows, velocity, etc.
/// </summary>
public sealed class MccRuleEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(8)]
    public string Mcc { get; set; } = "0000";

    public bool IsBlocked { get; set; } = false;

    /// <summary>
    /// Optional per-transaction cap. If null, no override.
    /// </summary>
    public decimal? PerTxnLimit { get; set; }

    [MaxLength(128)]
    public string? Description { get; set; }
}
