namespace BuildingBlocks.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;

    public string Topic { get; init; } = default!;
    public string Key { get; init; } = default!;
    public string PayloadJson { get; init; } = default!;

    public DateTimeOffset? ProcessedOn { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}