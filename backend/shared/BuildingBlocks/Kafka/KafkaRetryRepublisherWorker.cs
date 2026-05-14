using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Kafka;

/// <summary>
/// Consumes a <topic>.retry and republishes to the original topic after exponential backoff.
/// Keeps trace headers and re-signs message when signing secret is configured.
/// </summary>
public sealed class KafkaRetryRepublisherWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("BuildingBlocks.Kafka");

    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaRetryRepublisherWorker> _logger;

    private readonly string _retryTopic;
    private readonly string? _signingSecret;
    private readonly int _baseDelayMs;
    private readonly int _maxDelayMs;

    public KafkaRetryRepublisherWorker(
        string bootstrapServers,
        string groupId,
        string clientId,
        string retryTopic,
        ILogger<KafkaRetryRepublisherWorker> logger,
        string? signingSecret = null,
        int baseDelayMs = 500,
        int maxDelayMs = 30_000)
    {
        _logger = logger;
        _retryTopic = retryTopic;
        _signingSecret = string.IsNullOrWhiteSpace(signingSecret) ? null : signingSecret;
        _baseDelayMs = Math.Max(0, baseDelayMs);
        _maxDelayMs = Math.Max(_baseDelayMs, maxDelayMs);

        var c = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            ClientId = clientId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        var p = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = clientId + "-republisher",
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _consumer = new ConsumerBuilder<string, string>(c).Build();
        _producer = new ProducerBuilder<string, string>(p).Build();

        _consumer.Subscribe(_retryTopic);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = _consumer.Consume(stoppingToken);
                if (cr is null) continue;

                var parent = KafkaTracePropagation.Extract(cr.Message.Headers);
                using var activity = parent is null
                    ? ActivitySource.StartActivity("kafka.retry.republish", ActivityKind.Producer)
                    : ActivitySource.StartActivity("kafka.retry.republish", ActivityKind.Producer, parent.Value);

                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.destination", cr.Topic);

                // verify signature if enabled
                if (_signingSecret is not null && !KafkaMessageSecurity.Verify(cr.Message.Value, cr.Message.Headers, _signingSecret))
                {
                    _logger.LogWarning("Invalid signature in retry topic={Topic} key={Key} -> skipping", cr.Topic, cr.Message.Key);
                    _consumer.StoreOffset(cr);
                    _consumer.Commit(cr);
                    continue;
                }

                var originalTopic = GetAscii(cr.Message.Headers, "x-original-topic") ?? cr.Topic.Replace(".retry", "");
                var attempt = GetAttempt(cr.Message.Headers);
                var delay = ComputeBackoff(attempt);

                KafkaMetrics.KafkaRetryBackoffMs.Record(delay, new KeyValuePair<string, object?>("topic", originalTopic));

                if (delay > 0)
                    await Task.Delay(delay, stoppingToken);

                var headers = new Headers();
                foreach (var h in cr.Message.Headers)
                    headers.Add(h.Key, h.GetValueBytes());

                // Re-sign (optional) to ensure integrity
                if (_signingSecret is not null)
                    KafkaMessageSecurity.Sign(cr.Message.Value, headers, _signingSecret);

                KafkaTracePropagation.Inject(Activity.Current, headers);

                await _producer.ProduceAsync(originalTopic,
                    new Message<string, string> { Key = cr.Message.Key, Value = cr.Message.Value, Headers = headers },
                    stoppingToken);

                KafkaMetrics.KafkaRetryRepublishedTotal.Add(1, new KeyValuePair<string, object?>("topic", originalTopic));

                _consumer.StoreOffset(cr);
                _consumer.Commit(cr);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Retry republisher consume error topic={Topic}", _retryTopic);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry republisher error topic={Topic}", _retryTopic);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private int ComputeBackoff(int attempt)
    {
        if (attempt <= 0) return 0;
        // exponential backoff: baseDelay * 2^(attempt-1), capped
        var delay = _baseDelayMs * (1 << Math.Min(attempt - 1, 10));
        return Math.Min(delay, _maxDelayMs);
    }

    private static int GetAttempt(Headers headers)
    {
        var h = headers.LastOrDefault(x => x.Key.Equals("x-attempt", StringComparison.OrdinalIgnoreCase));
        if (h?.GetValueBytes() is { Length: > 0 } b && int.TryParse(System.Text.Encoding.ASCII.GetString(b), out var n))
            return n;
        return 0;
    }

    private static string? GetAscii(Headers headers, string key)
    {
        var last = headers.LastOrDefault(h => h.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (last is null) return null;
        return last.GetValueBytes() is { Length: > 0 } b ? System.Text.Encoding.ASCII.GetString(b) : null;
    }

    public override void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        _producer.Dispose();
        base.Dispose();
    }
}
