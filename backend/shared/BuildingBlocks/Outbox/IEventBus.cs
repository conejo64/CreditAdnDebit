namespace BuildingBlocks.Outbox;

public interface IEventBus
{
    Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct);
}