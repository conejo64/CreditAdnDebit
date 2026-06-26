using BuildingBlocks.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IsoSwitch.Infrastructure.Persistence;

namespace IsoSwitch.Infrastructure.Consumers;

public sealed class PciAuditConsumer : KafkaConsumerWorker
{
    private readonly ILogger<PciAuditConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public PciAuditConsumer(IConfiguration cfg, ILogger<PciAuditConsumer> logger, IServiceScopeFactory scopeFactory)
        : base(
            bootstrapServers: cfg["Kafka:BootstrapServers"] ?? "localhost:9092",
            groupId: cfg["Kafka:ConsumerGroupId"] ?? "isoswitch-pci-audit-consumer",
            clientId: cfg["Kafka:ClientId"] ?? "IsoSwitch.Api",
            topic: cfg["Kafka:Topics:PciAudit"] ?? "sw.audit.pci",
            logger: logger,
            signingSecret: cfg["Kafka:SigningSecret"],
            maxRetry: int.TryParse(cfg["Kafka:Retry:Max"], out var m) ? m : 3,
            retrySuffix: cfg["Kafka:Retry:Suffix"] ?? ".retry",
            dlqSuffix: cfg["Kafka:Dlq:Suffix"] ?? ".dlq")
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task HandleMessageAsync(string topic, string key, string value, Headers headers, CancellationToken ct)
    {
        _logger.LogInformation("PCI audit event consumed topic={Topic} key={Key} traceId={TraceId} payload={Payload}",
            topic, key, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "-", value);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        await audit.WriteAsync("kafka.consume.pci_audit", new { topic, key, payload = value }, correlationId: null, traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(), ct: ct);
    }
}
