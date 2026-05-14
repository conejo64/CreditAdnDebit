using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Kafka;

public abstract class KafkaConsumerWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("BuildingBlocks.Kafka");

    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _producer;
    private readonly string? _signingSecret;
    private readonly int _maxRetry;
    private readonly string _retrySuffix;
    private readonly string _dlqSuffix;
    private readonly ILogger _logger;
    private readonly string _topic;

    protected KafkaConsumerWorker(string bootstrapServers, string groupId, string clientId, string topic, ILogger logger, string? signingSecret = null, int maxRetry = 3, string retrySuffix = ".retry", string dlqSuffix = ".dlq")
    {
        _logger = logger;
        _topic = topic;

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            ClientId = clientId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();

        _signingSecret = string.IsNullOrWhiteSpace(signingSecret) ? null : signingSecret;
        _maxRetry = maxRetry < 0 ? 0 : maxRetry;
        _retrySuffix = retrySuffix;
        _dlqSuffix = dlqSuffix;

        var pconfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = clientId + "-consumer",
            Acks = Acks.All,
            EnableIdempotence = true
        };
        _producer = new ProducerBuilder<string, string>(pconfig).Build();

        _consumer.Subscribe(_topic);
    }

    protected abstract Task HandleMessageAsync(string topic, string key, string value, Headers headers, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Crucial: yield to host to allow Kestrel to start listening!
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = _consumer.Consume(stoppingToken);
                if (cr is null) continue;

                var parent = KafkaTracePropagation.Extract(cr.Message.Headers);
                using var activity = parent is null
                    ? ActivitySource.StartActivity("kafka.consume", ActivityKind.Consumer)
                    : ActivitySource.StartActivity("kafka.consume", ActivityKind.Consumer, parent.Value);

                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.destination", cr.Topic);
                activity?.SetTag("messaging.kafka.partition", cr.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", cr.Offset.Value);

                
                // v28 - verify signature if enabled
                if (_signingSecret is not null && !KafkaMessageSecurity.Verify(cr.Message.Value, cr.Message.Headers, _signingSecret))
                {
                    _logger.LogWarning("Invalid Kafka signature topic={Topic} key={Key} -> DLQ", cr.Topic, cr.Message.Key);
                    await PublishToDlqAsync(cr, "invalid_signature", stoppingToken);
                }
                else
                {
                    try
                    {
                        await HandleMessageAsync(cr.Topic, cr.Message.Key, cr.Message.Value, cr.Message.Headers, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Handler failure topic={Topic} key={Key}", cr.Topic, cr.Message.Key);
                        await PublishRetryOrDlqAsync(cr, "handler_exception", stoppingToken);
                    }
                }


                _consumer.StoreOffset(cr);
                _consumer.Commit(cr);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error topic={Topic}", _topic);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KafkaConsumerWorker error topic={Topic}", _topic);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    

    private static int GetAttempt(Headers headers)
    {
        var h = headers.LastOrDefault(x => x.Key.Equals("x-attempt", StringComparison.OrdinalIgnoreCase));
        if (h?.GetValueBytes() is { Length: > 0 } b && int.TryParse(System.Text.Encoding.ASCII.GetString(b), out var n))
            return n;
        return 0;
    }

    private async Task PublishRetryOrDlqAsync(ConsumeResult<string, string> cr, string reason, CancellationToken ct)
    {
        var attempt = GetAttempt(cr.Message.Headers) + 1;
        if (attempt <= _maxRetry)
        {
            var retryTopic = cr.Topic + _retrySuffix;
            await PublishAsync(retryTopic, cr.Message, attempt, reason, ct);
            KafkaMetrics.KafkaRetryPublishedTotal.Add(1, new KeyValuePair<string, object?>("topic", cr.Topic));
        }
        else
        {
            await PublishToDlqAsync(cr, reason, ct);
        }
    }

    private Task PublishToDlqAsync(ConsumeResult<string, string> cr, string reason, CancellationToken ct)
    {
        var dlqTopic = cr.Topic + _dlqSuffix;
        KafkaMetrics.KafkaDlqPublishedTotal.Add(1, new KeyValuePair<string, object?>("topic", cr.Topic));
        return PublishAsync(dlqTopic, cr.Message, GetAttempt(cr.Message.Headers), reason, ct);
    }

    private async Task PublishAsync(string topic, Message<string, string> msg, int attempt, string reason, CancellationToken ct)
    {
        // preserve headers + trace context; add attempt + reason
        var headers = new Headers();
        foreach (var h in msg.Headers)
            headers.Add(h.Key, h.GetValueBytes());

        headers.Remove("x-attempt");
        headers.Add("x-attempt", System.Text.Encoding.ASCII.GetBytes(attempt.ToString()));
        headers.Remove("x-failure-reason");
        headers.Remove("x-original-topic");
        headers.Add("x-original-topic", System.Text.Encoding.ASCII.GetBytes(_topic));
        headers.Add("x-failure-reason", System.Text.Encoding.ASCII.GetBytes(reason));

        // keep signature if configured (re-sign to avoid tampering)
        if (_signingSecret is not null)
            KafkaMessageSecurity.Sign(msg.Value, headers, _signingSecret);

        await _producer.ProduceAsync(topic, new Message<string, string> { Key = msg.Key, Value = msg.Value, Headers = headers }, ct);
    }

public override void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        _producer.Dispose();
        base.Dispose();
    }
}
