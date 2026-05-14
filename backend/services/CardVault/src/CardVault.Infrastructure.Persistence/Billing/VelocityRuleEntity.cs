using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

/// <summary>
/// Velocity limits per product.
/// Example: in a 15 minute window, allow max 5 auths and max $500.
/// </summary>
public sealed class VelocityRuleEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ProductCode { get; set; } = default!;

    public int WindowMinutes { get; set; } = 15;

    public int MaxCount { get; set; } = 10;

    public decimal MaxAmount { get; set; } = 1000m;

    [MaxLength(128)]
    public string? Description { get; set; }
}
