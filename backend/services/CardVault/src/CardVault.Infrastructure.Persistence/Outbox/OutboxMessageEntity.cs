using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Outbox;

public sealed class OutboxMessageEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; set; } = DateTimeOffset.UtcNow;

    public string Topic { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;

    public DateTimeOffset? ProcessedOn { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}