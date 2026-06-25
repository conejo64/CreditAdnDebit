using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IsoSwitch.Infrastructure.Messaging;

public sealed class SwitchEventPublisher : ISwitchEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _txTopic;
    private readonly string _isoTopic;
    private readonly string _auditTopic;

    public SwitchEventPublisher(IConfiguration cfg)
    {
        _txTopic = cfg.GetValue<string>("Kafka:Topics:TxEvents") ?? "sw.tx.events";
        _isoTopic = cfg.GetValue<string>("Kafka:Topics:IsoEvents") ?? "sw.iso.events";
        _auditTopic = cfg.GetValue<string>("Kafka:Topics:AuditEvents") ?? "sw.iso.audit";

        var conf = new ProducerConfig
        {
            BootstrapServers = cfg.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092",
            Acks = Acks.All
        };

        _producer = new ProducerBuilder<string, string>(conf).Build();
    }

    public Task PublishTxAsync(string key, object payload, CancellationToken ct)
        => PublishAsync(_txTopic, key, payload, ct);

    public Task PublishIsoAsync(string key, object payload, CancellationToken ct)
        => PublishAsync(_isoTopic, key, payload, ct);

    public Task PublishAuditAsync(string key, object payload, CancellationToken ct)
        => PublishAsync(_auditTopic, key, payload, ct);

    private async Task PublishAsync(string topic, string key, object payload, CancellationToken ct)
    {
        try 
        {
            var json = JsonSerializer.Serialize(payload);
            await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = json }, ct);
        }
        catch (Exception ex)
        {
            // Fail-safe: log but don't crash the main transaction flow
            Console.WriteLine($"[KAFKA_ERROR] Failed to publish to {topic}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(2));
        _producer.Dispose();
    }
}