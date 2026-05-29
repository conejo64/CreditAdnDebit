using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Collections;

/// <summary>
/// Records an internal operator note against a delinquency record.
/// Immutable by design — no edit or delete operations are exposed.
/// </summary>
public sealed class DelinquencyNoteEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the DelinquencyRecordEntity this note belongs to.</summary>
    public Guid DelinquencyRecordId { get; set; }

    /// <summary>Note body text (max 1000 chars).</summary>
    [Required, MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>Identity (email or user ID) of the operator who created the note.</summary>
    [Required, MaxLength(256)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when the note was created (UTC).</summary>
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
