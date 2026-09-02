using BuildingBlocks.Commercial;

namespace IsoSwitch.Api.Background;

/// <summary>
/// One audit record the switch writes as it boots in commercial mode.
/// </summary>
public sealed record CommercialStartupAuditRecord(string EventType, object Payload, string CorrelationId);

/// <summary>
/// Decides which startup audit records commercial mode owes the audit trail.
/// </summary>
/// <remarks>
/// The decision is kept separate from the write so it can be covered without booting
/// the API: composing the records is the part with rules in it, while writing them is
/// a loop over <c>AuditService.WriteAsync</c> in the composition root.
/// </remarks>
public static class CommercialStartupAudit
{
    private const string CorrelationId = "commercial-startup";
    private const string SimulatorConnectorId = "SIMULATOR";

    /// <summary>
    /// Returns the records to write for <paramref name="options"/>, or an empty list
    /// outside commercial mode — demo mode keeps the simulator, so there is no denial
    /// to record and no commercial boot to attest.
    /// </summary>
    public static IReadOnlyList<CommercialStartupAuditRecord> Build(CommercialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsCommercialMode)
        {
            return [];
        }

        return
        [
            new CommercialStartupAuditRecord(
                "commercial.mode.started",
                new
                {
                    mode = options.Mode.ToString(),
                    demoSurfacesEnabled = options.EnableDemoSurfaces,
                    swaggerEnabled = options.EnableSwagger,
                    anonymousDiagnosticsEnabled = options.EnableAnonymousDiagnostics,
                    claimRegisterVersion = options.ClaimRegisterVersion
                },
                CorrelationId),

            new CommercialStartupAuditRecord(
                "commercial.simulator.registration_denied",
                new
                {
                    connectorId = SimulatorConnectorId,
                    reason = "Commercial mode excludes simulator connector registration."
                },
                CorrelationId)
        ];
    }
}
