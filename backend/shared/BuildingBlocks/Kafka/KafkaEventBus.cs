using System.Diagnostics;
using BuildingBlocks.Outbox;
using Confluent.Kafka;

namespace BuildingBlocks.Kafka;

public sealed class KafkaEventBus : IEventBus, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("BuildingBlocks.Kafka");

    private readonly IProducer<string, string> _producer;
    private readonly string? _signingSecret;

    public KafkaEventBus(string bootstrapServers, string clientId, string? signingSecret = null)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = clientId,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
        _signingSecret = string.IsNullOrWhiteSpace(signingSecret) ? null : signingSecret;
    }

    public async Task PublishAsync(string topic, string key, string payloadJson, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("kafka.produce", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.kafka.message_key", key);

        var headers = new Headers();
        KafkaTracePropagation.Inject(Activity.Current, headers);
        if (_signingSecret is not null)
            KafkaMessageSecurity.Sign(payloadJson, headers, _signingSecret);

        var msg = new Message<string, string>
        {
            Key = key,
            Value = payloadJson,
            Headers = headers
        };

        await _producer.ProduceAsync(topic, msg, ct);
    }

    public void Dispose() => _producer.Dispose();
}
