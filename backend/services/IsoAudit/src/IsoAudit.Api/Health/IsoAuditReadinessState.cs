namespace IsoAudit.Api.Health;

public static class IsoAuditReadinessChecks
{
    public const string Database = "database";
    public const string Kafka = "kafka";
    public const string KafkaTopic = "kafka-topic";
    public const string Consumer = "consumer";

    public static readonly string[] Required = [Database, Kafka, KafkaTopic, Consumer];
}

public static class IsoAuditReadinessStatus
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Unready = "unready";
    public const string Failed = "failed";
}

public sealed record IsoAuditCheckStatus(string Name, string Status, string? Reason);

public sealed record IsoAuditReadinessSnapshot(string Status, string Service, IReadOnlyList<IsoAuditCheckStatus> Checks);

public interface IIsoAuditReadinessState
{
    IsoAuditReadinessSnapshot GetSnapshot();
    void Mark(string name, string status, string? reason = null);
    void MarkStarting(string name, string? reason = null);
    void MarkReady(string name);
    void MarkUnready(string name, string? reason = null);
    void MarkFailed(string name, string? reason = null);
}

public sealed class IsoAuditReadinessState : IIsoAuditReadinessState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IsoAuditCheckStatus> _checks = new(StringComparer.OrdinalIgnoreCase);

    public IsoAuditReadinessState()
    {
        foreach (var name in IsoAuditReadinessChecks.Required)
        {
            _checks[name] = new IsoAuditCheckStatus(name, IsoAuditReadinessStatus.Starting, null);
        }
    }

    public IsoAuditReadinessSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var checks = IsoAuditReadinessChecks.Required
                .Select(name => _checks.TryGetValue(name, out var check)
                    ? check
                    : new IsoAuditCheckStatus(name, IsoAuditReadinessStatus.Starting, null))
                .ToArray();

            var status = checks.All(c => string.Equals(c.Status, IsoAuditReadinessStatus.Ready, StringComparison.OrdinalIgnoreCase))
                ? IsoAuditReadinessStatus.Ready
                : IsoAuditReadinessStatus.Unready;

            return new IsoAuditReadinessSnapshot(status, "IsoAudit.Api", checks);
        }
    }

    public void Mark(string name, string status, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        lock (_gate)
        {
            _checks[name] = new IsoAuditCheckStatus(name, status, SanitizeReason(name, status, reason));
        }
    }

    public void MarkStarting(string name, string? reason = null) => Mark(name, IsoAuditReadinessStatus.Starting, reason);

    public void MarkReady(string name) => Mark(name, IsoAuditReadinessStatus.Ready);

    public void MarkUnready(string name, string? reason = null) => Mark(name, IsoAuditReadinessStatus.Unready, reason);

    public void MarkFailed(string name, string? reason = null) => Mark(name, IsoAuditReadinessStatus.Failed, reason);

    private static string? SanitizeReason(string name, string status, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || string.Equals(status, IsoAuditReadinessStatus.Ready, StringComparison.OrdinalIgnoreCase))
            return null;

        var lowered = reason.ToLowerInvariant();
        if (lowered.Contains("password") || lowered.Contains("connectionstring") || lowered.Contains("host=") ||
            lowered.Contains("username=") || lowered.Contains("jwt") || lowered.Contains("token") ||
            lowered.Contains("pan") || lowered.Contains("pin") || lowered.Contains("exception") ||
            lowered.Contains(" at "))
        {
            return name switch
            {
                IsoAuditReadinessChecks.Database => "database unavailable",
                IsoAuditReadinessChecks.Kafka => "kafka unavailable",
                IsoAuditReadinessChecks.KafkaTopic => "audit topic unavailable",
                IsoAuditReadinessChecks.Consumer => "consumer unavailable",
                _ => "dependency unavailable"
            };
        }

        return reason.Length <= 160 ? reason : reason[..160];
    }
}