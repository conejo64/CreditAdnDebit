using Microsoft.Extensions.Options;

namespace IsoAudit.Api.Health;

public sealed class IsoAuditKafkaReadinessWorker : BackgroundService
{
    private readonly IsoAuditKafkaTopicProbe _probe;
    private readonly IIsoAuditReadinessState _state;
    private readonly IOptions<IsoAuditReadinessOptions> _options;
    private readonly ILogger<IsoAuditKafkaReadinessWorker> _logger;

    public IsoAuditKafkaReadinessWorker(
        IsoAuditKafkaTopicProbe probe,
        IIsoAuditReadinessState state,
        IOptions<IsoAuditReadinessOptions> options,
        ILogger<IsoAuditKafkaReadinessWorker> logger)
    {
        _probe = probe;
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.MarkStarting(IsoAuditReadinessChecks.Kafka);
        _state.MarkStarting(IsoAuditReadinessChecks.KafkaTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _probe.CheckAsync(stoppingToken);
                _state.MarkReady(IsoAuditReadinessChecks.Kafka);

                if (string.Equals(result.Status, IsoAuditKafkaTopicProbeResult.Ready, StringComparison.OrdinalIgnoreCase))
                    _state.MarkReady(IsoAuditReadinessChecks.KafkaTopic);
                else
                    _state.MarkUnready(IsoAuditReadinessChecks.KafkaTopic, result.Reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IsoAudit Kafka readiness check failed");
                _state.MarkUnready(IsoAuditReadinessChecks.Kafka, "kafka unavailable");
                _state.MarkUnready(IsoAuditReadinessChecks.KafkaTopic, "audit topic unavailable");
            }

            try
            {
                await Task.Delay(_options.Value.ProbeInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}