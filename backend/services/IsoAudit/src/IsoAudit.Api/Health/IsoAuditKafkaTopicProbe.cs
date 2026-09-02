using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace IsoAudit.Api.Health;

public static class IsoAuditKafkaTopicProbeResult
{
    public const string Ready = "ready";
    public const string Missing = "missing";
}

public sealed record IsoAuditKafkaTopicProbeEvaluation(string Status, string? Reason);

public sealed class IsoAuditKafkaTopicProbe
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<IsoAuditReadinessOptions> _options;
    private readonly Func<AdminClientConfig, TimeSpan, IReadOnlyCollection<string>> _readAllTopicNames;

    public IsoAuditKafkaTopicProbe(IConfiguration configuration, IOptions<IsoAuditReadinessOptions> options)
        : this(configuration, options, ReadAllTopicNames)
    {
    }

    internal IsoAuditKafkaTopicProbe(
        IConfiguration configuration,
        IOptions<IsoAuditReadinessOptions> options,
        Func<AdminClientConfig, TimeSpan, IReadOnlyCollection<string>> readAllTopicNames)
    {
        _configuration = configuration;
        _options = options;
        _readAllTopicNames = readAllTopicNames;
    }

    public async Task<IsoAuditKafkaTopicProbeEvaluation> CheckAsync(CancellationToken cancellationToken)
    {
        var bootstrapServers = _configuration.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";
        var requiredTopic = ResolveRequiredTopic(_configuration, _options.Value);
        var config = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            AllowAutoCreateTopics = false
        };

        var topics = await Task
            .Run(() => _readAllTopicNames(config, _options.Value.KafkaMetadataTimeout), CancellationToken.None)
            .WaitAsync(cancellationToken);

        return EvaluateTopicMetadata(topics, requiredTopic);
    }

    public static IsoAuditKafkaTopicProbeEvaluation EvaluateTopicMetadata(IEnumerable<string> topicNames, string requiredTopic)
    {
        var exists = topicNames.Any(topic => string.Equals(topic, requiredTopic, StringComparison.Ordinal));
        return exists
            ? new IsoAuditKafkaTopicProbeEvaluation(IsoAuditKafkaTopicProbeResult.Ready, null)
            : new IsoAuditKafkaTopicProbeEvaluation(IsoAuditKafkaTopicProbeResult.Missing, $"missing topic {requiredTopic}");
    }

    internal static string ResolveRequiredTopic(IConfiguration configuration, IsoAuditReadinessOptions options) =>
        configuration.GetValue<string>("Kafka:Topics:AuditEvents") ?? options.RequiredTopic;

    private static IReadOnlyCollection<string> ReadAllTopicNames(AdminClientConfig config, TimeSpan timeout)
    {
        using var admin = new AdminClientBuilder(config).Build();
        return admin.GetMetadata(timeout).Topics.Select(topic => topic.Topic).ToArray();
    }
}