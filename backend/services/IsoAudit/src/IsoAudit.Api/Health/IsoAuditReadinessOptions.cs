namespace IsoAudit.Api.Health;

public sealed class IsoAuditReadinessOptions
{
    public const string SectionName = "IsoAudit:Readiness";

    public TimeSpan StartupGracePeriod { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan DatabaseTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KafkaMetadataTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public string RequiredTopic { get; init; } = "sw.iso.audit";
}