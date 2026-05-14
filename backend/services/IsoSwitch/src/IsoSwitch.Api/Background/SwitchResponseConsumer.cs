using System;
using System.Collections.Concurrent;
using Confluent.Kafka;
using IsoSwitch.Api.Iso8583;
using IsoSwitch.Api.Tcp;

namespace IsoSwitch.Api.Background;

public sealed class SwitchResponseConsumer : BackgroundService
{
    public static readonly ConcurrentQueue<string> LastResponses = new();

    private readonly ILogger<SwitchResponseConsumer> _logger;
    private readonly IConfiguration _cfg;

    public SwitchResponseConsumer(ILogger<SwitchResponseConsumer> logger, IConfiguration cfg)
    {
        _logger = logger;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrap = _cfg["Kafka:BootstrapServers"] ?? "localhost:9092";
        var topic = _cfg["Kafka:SwitchResponseTopic"] ?? "switch-responses";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = _cfg["Kafka:ResponseConsumerGroupId"] ?? "isoswitch-responses",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        _logger.LogInformation("SwitchResponseConsumer subscribed to {Topic}", topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(TimeSpan.FromSeconds(1));
                if (cr is null) continue;

                // keep last 100
                LastResponses.Enqueue(cr.Message.Value);
                while (LastResponses.Count > 100 && LastResponses.TryDequeue(out _)) { }

                _logger.LogInformation("Received switch response: key={Key} payload={Payload}", cr.Message.Key, cr.Message.Value);

                // v46/v47 - build ISO8583 0110 response if this is auth.response
                if (IsoResponseBuilder.TryParseAuthResponseEnvelope(cr.Message.Value, out var ar) && ar is not null)
                {
                    IsoSwitch.Api.Tcp.SwitchResponseAwaiter.TryComplete(ar);
                }

            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Consume error");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-fatal response consumer failure");
            }

            await Task.Yield();
        }
    }
}
