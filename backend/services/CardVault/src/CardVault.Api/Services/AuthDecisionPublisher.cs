using BuildingBlocks.Outbox;
using System.Text.Json;

namespace CardVault.Api.Services;

public sealed class AuthDecisionPublisher
{
    private readonly IConfiguration _cfg;
    private readonly IEventBus _bus;

    public AuthDecisionPublisher(IConfiguration cfg, IEventBus bus)
    {
        _cfg = cfg;
        _bus = bus;
    }

    public Task PublishAuthResponseAsync(string key, object payload, CancellationToken ct)
    {
        var topic = _cfg["Kafka:SwitchResponseTopic"] ?? "switch-responses";
        var env = JsonSerializer.Serialize(new
        {
            eventName = "switch.v1.auth.response",
            eventId = Guid.NewGuid().ToString("N"),
            occurredOn = DateTimeOffset.UtcNow,
            payload
        });
        return _bus.PublishAsync(topic, key, env, ct);
    }
}
