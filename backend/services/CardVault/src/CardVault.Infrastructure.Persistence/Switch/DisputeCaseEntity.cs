using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Switch;

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


/// <summary>
/// Simplified dispute/chargeback case (for demo).
/// </summary>
public sealed class DisputeCaseEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    [MaxLength(16)]
    public string Network { get; set; } = default!;

    [MaxLength(12)]
    public string Rrn { get; set; } = default!;

    [MaxLength(64)]
    public string ReasonCode { get; set; } = default!;

    public decimal Amount { get; set; }

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;

    public DateTimeOffset OpenedOn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedOn { get; set; }
}
