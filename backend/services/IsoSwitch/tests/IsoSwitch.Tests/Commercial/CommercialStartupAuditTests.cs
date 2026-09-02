using BuildingBlocks.Commercial;
using IsoSwitch.Api.Background;

namespace IsoSwitch.Tests.Commercial;

/// <summary>
/// Covers task 2.4: the startup audit trail that records commercial-mode boot and
/// the simulator registration that commercial mode refuses.
/// </summary>
public sealed class CommercialStartupAuditTests
{
    [Fact]
    public void CommercialMode_EmitsStartupAndSimulatorDenialRecords()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            EnableDemoSurfaces = false,
            EnableSwagger = false,
            EnableAnonymousDiagnostics = false,
            ClaimRegisterVersion = "2026.08"
        };

        var records = CommercialStartupAudit.Build(options);

        Assert.Collection(
            records,
            first => Assert.Equal("commercial.mode.started", first.EventType),
            second => Assert.Equal("commercial.simulator.registration_denied", second.EventType));
        Assert.All(records, r => Assert.Equal("commercial-startup", r.CorrelationId));
    }

    [Fact]
    public void CommercialMode_StartupRecordCarriesTheEffectiveExposureFlags()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            EnableDemoSurfaces = false,
            EnableSwagger = false,
            EnableAnonymousDiagnostics = false,
            ClaimRegisterVersion = "2026.08"
        };

        var startup = CommercialStartupAudit.Build(options)[0];
        var payload = startup.Payload.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(startup.Payload));

        Assert.Equal("Commercial", payload["mode"]);
        Assert.Equal(false, payload["demoSurfacesEnabled"]);
        Assert.Equal(false, payload["swaggerEnabled"]);
        Assert.Equal(false, payload["anonymousDiagnosticsEnabled"]);
        Assert.Equal("2026.08", payload["claimRegisterVersion"]);
    }

    [Fact]
    public void CommercialMode_DenialRecordNamesTheRefusedConnector()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            ClaimRegisterVersion = "2026.08"
        };

        var denial = CommercialStartupAudit.Build(options)[1];
        var payload = denial.Payload.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(denial.Payload));

        Assert.Equal("SIMULATOR", payload["connectorId"]);
        Assert.NotNull(payload["reason"]);
    }

    [Fact]
    public void DemoMode_EmitsNothing()
    {
        // Demo mode keeps the simulator, so there is no denial to record and no
        // commercial boot to attest. Writing records here would put a false
        // commercial-mode claim in the audit trail.
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Demo,
            EnableDemoSurfaces = true,
            ClaimRegisterVersion = "legacy-demo"
        };

        Assert.Empty(CommercialStartupAudit.Build(options));
    }
}
