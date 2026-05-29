using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Collections;

public enum ContactChannel
{
    Phone    = 1,
    Email    = 2,
    SMS      = 3,
    InPerson = 4
}

public enum ContactOutcome
{
    Contacted       = 1,
    NoAnswer        = 2,
    InvalidContact  = 3,
    CustomerRefused = 4
}

/// <summary>
/// Records a single operator contact attempt against a delinquency record.
/// Immutable by design — no edit or delete operations are exposed.
/// </summary>
public sealed class ContactAttemptEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the DelinquencyRecordEntity this attempt belongs to.</summary>
    public Guid DelinquencyRecordId { get; set; }

    /// <summary>Communication channel used for this attempt.</summary>
    public ContactChannel Channel { get; set; }

    /// <summary>Result of the contact attempt.</summary>
    public ContactOutcome Outcome { get; set; }

    /// <summary>Optional free-text notes about the attempt (max 1000 chars).</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Identity (email or user ID) of the operator who registered the attempt.</summary>
    [Required, MaxLength(256)]
    public string AttemptedBy { get; set; } = string.Empty;

    /// <summary>Timestamp of the contact attempt (UTC).</summary>
    public DateTimeOffset AttemptedOn { get; set; } = DateTimeOffset.UtcNow;
}
