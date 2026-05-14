using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CardVault.Api;

public static class Observability
{
    public static readonly ActivitySource ActivitySource = new("CardVault");
    public static readonly Meter Meter = new("CardVault.Metrics", "1.0.0");

    public static readonly Counter<long> VaultOperationsTotal =
        Meter.CreateCounter<long>("vault_operations_total", description: "Total vault operations");

    public static readonly Histogram<double> VaultOperationDurationMs =
        Meter.CreateHistogram<double>("vault_operation_duration_ms", unit: "ms", description: "Vault operation duration");
}