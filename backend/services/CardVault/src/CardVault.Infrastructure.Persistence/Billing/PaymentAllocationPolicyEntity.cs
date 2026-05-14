using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum AllocationBucket
{
    Interest = 1,
    Fees = 2,
    Principal = 3
}

/// <summary>
/// Defines how payments are allocated across buckets.
/// Simplified: ordered buckets with optional weights (not used in v37, order only).
/// </summary>
public sealed class PaymentAllocationPolicyEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = "DEFAULT";

    // Comma separated order e.g. "Interest,Fees,Principal"
    [MaxLength(128)]
    public string Order { get; set; } = "Interest,Fees,Principal";

    public bool IsDefault { get; set; } = true;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
