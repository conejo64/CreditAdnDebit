using BuildingBlocks.Commercial;
using FluentAssertions;
using IsoSwitch.Application.Config;
using IsoSwitch.Infrastructure.Persistence.Routing;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IsoSwitch.Tests.Commercial;

public class CommercialConnectorRegistryTests
{
    [Fact]
    public void CommercialMode_WithForceSimulatorConfiguration_FailsClosed()
    {
        var connector = Substitute.For<IAcquirerConnector>();
        connector.ConnectorId.Returns("TCP-GATEWAY");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Iso:ForceSimulator"] = "true"
            })
            .Build();

        var act = () => new ConnectorRegistry(
            [connector],
            configuration,
            Options.Create(new CommercialOptions { Mode = CommercialMode.Commercial }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Iso:ForceSimulator*commercial mode*");
    }

    [Fact]
    public void CommercialMode_UnregisteredConnector_DoesNotFallbackToSimulator()
    {
        var simulator = Substitute.For<IAcquirerConnector>();
        simulator.ConnectorId.Returns("SIMULATOR");
        var gateway = Substitute.For<IAcquirerConnector>();
        gateway.ConnectorId.Returns("TCP-GATEWAY");

        var registry = new ConnectorRegistry(
            [gateway, simulator],
            new ConfigurationBuilder().Build(),
            Options.Create(new CommercialOptions { Mode = CommercialMode.Commercial }));

        var act = () => registry.Get("UNKNOWN-GATEWAY");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UNKNOWN-GATEWAY*");
    }

    [Fact]
    public void DemoMode_UnregisteredConnector_PreservesSimulatorFallback()
    {
        var simulator = Substitute.For<IAcquirerConnector>();
        simulator.ConnectorId.Returns("SIMULATOR");

        var registry = new ConnectorRegistry(
            [simulator],
            new ConfigurationBuilder().Build(),
            Options.Create(new CommercialOptions { Mode = CommercialMode.Demo, EnableDemoSurfaces = true }));

        var connector = registry.Get("UNKNOWN-GATEWAY");

        connector.ConnectorId.Should().Be("SIMULATOR");
    }
}
