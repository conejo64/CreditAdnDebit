using System.Diagnostics.Metrics;

namespace BuildingBlocks.Kafka;

public static class KafkaMetrics
{
    public static readonly Meter Meter = new("BuildingBlocks.Kafka.Metrics", "1.0.0");

    public static readonly Counter<long> KafkaRetryPublishedTotal =
        Meter.CreateCounter<long>("kafka_retry_published_total", description: "Messages published to retry topics");

    public static readonly Counter<long> KafkaDlqPublishedTotal =
        Meter.CreateCounter<long>("kafka_dlq_published_total", description: "Messages published to dlq topics");

    public static readonly Counter<long> KafkaRetryRepublishedTotal =
        Meter.CreateCounter<long>("kafka_retry_republished_total", description: "Messages republished from retry topics back to main topics");

    public static readonly Histogram<double> KafkaRetryBackoffMs =
        Meter.CreateHistogram<double>("kafka_retry_backoff_ms", unit: "ms", description: "Backoff applied before republish");
}
