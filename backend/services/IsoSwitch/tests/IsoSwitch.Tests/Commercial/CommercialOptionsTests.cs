using System.Text.Json;
using BuildingBlocks.Commercial;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Commercial;

public class CommercialOptionsTests
{
    [Fact]
    public void DefaultConstruction_IsCommercialAndFailClosed()
    {
        var options = new CommercialOptions();

        Assert.Equal(CommercialMode.Commercial, options.Mode);
        Assert.False(options.EnableDemoSurfaces);
        Assert.False(options.EnableAnonymousDiagnostics);
        Assert.False(options.EnableSwagger);
        Assert.Equal("unpublished", options.ClaimRegisterVersion);
        Assert.True(options.IsCommercialMode);
        Assert.False(options.CanExposeDemoSurfaces);
        Assert.False(options.CanExposeAnonymousDiagnostics);
        Assert.False(options.CanExposeSwagger);
    }

    [Fact]
    public void CommercialMode_WithAnyPublicExposureFlag_IsRejected()
    {
        var validator = new CommercialOptionsValidator();
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Commercial,
            EnableDemoSurfaces = true,
            EnableAnonymousDiagnostics = true,
            EnableSwagger = true,
            ClaimRegisterVersion = "2026.08"
        };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains("Commercial", string.Join(" ", result.Failures));
        Assert.Contains("EnableDemoSurfaces", string.Join(" ", result.Failures));
        Assert.Contains("EnableAnonymousDiagnostics", string.Join(" ", result.Failures));
        Assert.Contains("EnableSwagger", string.Join(" ", result.Failures));
    }

    [Fact]
    public void DemoMode_WithExplicitFlags_AllowsOnlyRequestedSurfaces()
    {
        var options = new CommercialOptions
        {
            Mode = CommercialMode.Demo,
            EnableDemoSurfaces = true,
            EnableSwagger = true,
            EnableAnonymousDiagnostics = false,
            ClaimRegisterVersion = "demo-2026.08"
        };

        var result = new CommercialOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
        Assert.False(options.IsCommercialMode);
        Assert.True(options.CanExposeDemoSurfaces);
        Assert.True(options.CanExposeSwagger);
        Assert.False(options.CanExposeAnonymousDiagnostics);
    }


    [Fact]
    public async Task CommercialMode_WithExposureFlag_FailsHostStartupValidation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Commercial:Mode"] = "Commercial",
            ["Commercial:EnableSwagger"] = "true",
            ["Commercial:ClaimRegisterVersion"] = "2026.08"
        });
        builder.Services.AddCommercialOptions();

        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Commercial mode", exception.Message);
        Assert.Contains("EnableSwagger", exception.Message);
    }
    [Fact]
    public void AddCommercialOptions_BindsConfigurationAndValidatesOnStart()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Commercial:Mode"] = "Demo",
                ["Commercial:EnableDemoSurfaces"] = "true",
                ["Commercial:ClaimRegisterVersion"] = "local-demo"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCommercialOptions();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CommercialOptions>>().Value;

        Assert.Equal(CommercialMode.Demo, options.Mode);
        Assert.True(options.EnableDemoSurfaces);
        Assert.Equal("local-demo", options.ClaimRegisterVersion);
        Assert.Contains(provider.GetServices<IValidateOptions<CommercialOptions>>(), v => v is CommercialOptionsValidator);
    }
}

public class ClaimRegisterContractTests
{
    [Fact]
    public void PublicDisclosure_DoesNotExposeInternalEvidenceMetadata()
    {
        var claim = new ClaimRegisterEntry
        {
            CapabilityId = "iso-simulator",
            Label = "ISO simulator",
            Maturity = ClaimMaturity.Simulation,
            PermittedModes = [CommercialMode.Demo],
            CommercialMessage = "Simulator is available only for synthetic, non-money-moving demos.",
            Owner = "switch-team",
            EvidenceUri = new Uri("https://internal.example/evidence/iso-simulator"),
            EvidenceHash = "sha256:secret",
            ReviewedBy = "risk-reviewer",
            ReviewedAtUtc = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            ExpiresAtUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            InternalNotes = "contains governance-only evidence details"
        };

        var dto = CommercialDisclosureDto.FromClaim(claim);
        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("iso-simulator", json);
        Assert.Contains("Simulation", json);
        Assert.DoesNotContain("switch-team", json);
        Assert.DoesNotContain("EvidenceUri", json);
        Assert.DoesNotContain("EvidenceHash", json);
        Assert.DoesNotContain("ReviewedBy", json);
        Assert.DoesNotContain("InternalNotes", json);
    }

    [Fact]
    public void VerifiedClaimWithoutEvidence_IsRejectedByRegisterValidator()
    {
        var claim = new ClaimRegisterEntry
        {
            CapabilityId = "hsm-routing",
            Label = "HSM routing",
            Maturity = ClaimMaturity.Verified,
            PermittedModes = [CommercialMode.Commercial],
            CommercialMessage = "Verified for commercial use.",
            Owner = "switch-team",
            ReviewedBy = "risk-reviewer"
        };

        var result = ClaimRegisterValidator.Validate([claim]);

        Assert.False(result.IsSuccess);
        Assert.Contains("hsm-routing", result.Error);
        Assert.Contains("evidence", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClaimWithoutOwner_IsRejectedByRegisterValidator()
    {
        var claim = new ClaimRegisterEntry
        {
            CapabilityId = "audit-export",
            Label = "Audit export",
            Maturity = ClaimMaturity.Simulation,
            PermittedModes = [CommercialMode.Demo],
            CommercialMessage = "Available only for controlled demos.",
            Owner = "   ",
            ReviewedBy = "risk-reviewer"
        };

        var result = ClaimRegisterValidator.Validate([claim]);

        Assert.False(result.IsSuccess);
        Assert.Contains("audit-export", result.Error);
        Assert.Contains("owner", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifiedClaimWithEvidence_IsAcceptedByRegisterValidator()
    {
        var claim = new ClaimRegisterEntry
        {
            CapabilityId = "audit-export",
            Label = "Audit export",
            Maturity = ClaimMaturity.Verified,
            PermittedModes = [CommercialMode.Commercial],
            CommercialMessage = "Verified for commercial evidence export.",
            Owner = "audit-team",
            EvidenceHash = "sha256:abc123",
            ReviewedBy = "risk-reviewer"
        };

        var result = ClaimRegisterValidator.Validate([claim]);

        Assert.True(result.IsSuccess);
    }
}

public class CommercialConfigurationDefaultsTests
{

    [Fact]
    public void BaseIsoSwitchConfig_DoesNotForceSimulatorOrDeclareSimulatorConnector()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(GetRepositoryRoot(), "backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json"), optional: false)
            .Build();

        Assert.False(configuration.GetValue<bool>("Iso:ForceSimulator"));
        Assert.False(configuration.GetSection("Iso:Connectors:Connectors:SIMULATOR").Exists());
        Assert.False(configuration.GetSection("IsoSimulator").Exists());
    }
    [Theory]
    [InlineData("backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json")]
    [InlineData("backend/services/CardVault/src/CardVault.Api/appsettings.json")]
    [InlineData("backend/services/IsoAudit/src/IsoAudit.Api/appsettings.json")]
    public void ServiceDefaultConfig_IsCommercialAndFailClosed(string relativePath)
    {
        var options = LoadCommercialOptions(relativePath);

        Assert.Equal(CommercialMode.Commercial, options.Mode);
        Assert.False(options.EnableDemoSurfaces);
        Assert.False(options.EnableAnonymousDiagnostics);
        Assert.False(options.EnableSwagger);
    }

    [Theory]
    [InlineData("backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.Development.json")]
    [InlineData("backend/services/CardVault/src/CardVault.Api/appsettings.Development.json")]
    [InlineData("backend/services/IsoAudit/src/IsoAudit.Api/appsettings.Development.json")]
    public void DevelopmentConfig_ExplicitlyOptsIntoDemoWithoutAnonymousDiagnostics(string relativePath)
    {
        var options = LoadCommercialOptions(relativePath);

        Assert.Equal(CommercialMode.Demo, options.Mode);
        Assert.True(options.EnableDemoSurfaces);
        Assert.False(options.EnableAnonymousDiagnostics);
    }

    private static CommercialOptions LoadCommercialOptions(string relativePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(GetRepositoryRoot(), relativePath), optional: false)
            .Build();

        var options = new CommercialOptions();
        configuration.GetSection(CommercialOptions.Section).Bind(options);
        return options;
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}





