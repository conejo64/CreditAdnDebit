using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Accounting;

public sealed class JournalEntryEntity
{
    [Key]
    public Guid Id { get; set; }

    public DateOnly BusinessDate { get; set; }

    [MaxLength(30)]
    public string SourceModule { get; set; } = default!;

    [MaxLength(100)]
    public string SourceReference { get; set; } = default!;

    [MaxLength(50)]
    public string EventType { get; set; } = default!;

    [MaxLength(250)]
    public string Description { get; set; } = default!;

    [MaxLength(20)]
    public string Status { get; set; } = "POSTED";

    [MaxLength(64)]
    public string? TraceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PostedAt { get; set; }

    public List<JournalEntryLineEntity> Lines { get; set; } = new();
}
