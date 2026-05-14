using IsoSwitch.Api;
using IsoSwitch.Api.Features.Transactions.Commands.NetworkManagement;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using FluentAssertions;
using NSubstitute;

namespace IsoSwitch.Tests.Features.Transactions.Commands.NetworkManagement;

public class NetworkCommandHandlerTests
{
    private readonly ConnectorRegistry _registry;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly IAcquirerConnector _connector;
    private readonly NetworkCommandHandler _sut;

    public NetworkCommandHandlerTests()
    {
        _connector = Substitute.For<IAcquirerConnector>();
        _connector.ConnectorId.Returns("SIMULATOR");
        
        _registry = new ConnectorRegistry(new[] { _connector });
        _publisher = Substitute.For<ISwitchEventPublisher>();
        _audit = Substitute.For<IIsoAuditService>();

        _sut = new NetworkCommandHandler(_registry, _publisher, _audit);
    }

    [Fact]
    public async Task Handle_Ping_ShouldSend301()
    {
        // Arrange
        var traceId = "ping-123";
        var command = new NetworkCommand(traceId, NetworkOperation.Ping);

        var responseIso = new IsoMessage { Mti = "0810" };
        responseIso.Set(39, "00");

        _connector.AuthorizeAsync(Arg.Is<IsoMessage>(m => m.Fields[70] == "301"), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Mti.Should().Be("0810");
        result.ResponseCode.Should().Be("00");
    }

    [Fact]
    public async Task Handle_SignOff_ShouldSend302()
    {
        // Arrange
        var traceId = "off-456";
        var command = new NetworkCommand(traceId, NetworkOperation.SignOff);

        var responseIso = new IsoMessage { Mti = "0810" };
        responseIso.Set(39, "00");

        _connector.AuthorizeAsync(Arg.Is<IsoMessage>(m => m.Fields[70] == "302"), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Mti.Should().Be("0810");
        result.ResponseCode.Should().Be("00");
    }

    [Fact]
    public async Task Handle_SignOn_ShouldSend301()
    {
        // Arrange
        var traceId = "on-789";
        var command = new NetworkCommand(traceId, NetworkOperation.SignOn);

        var responseIso = new IsoMessage { Mti = "0810" };
        responseIso.Set(39, "00");

        _connector.AuthorizeAsync(Arg.Is<IsoMessage>(m => m.Fields[70] == "301"), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Mti.Should().Be("0810");
        result.ResponseCode.Should().Be("00");
    }

    [Fact]
    public async Task Handle_ConnectorThrows_ShouldRethrow()
    {
        // Arrange
        var traceId = "err-999";
        var command = new NetworkCommand(traceId, NetworkOperation.Ping);

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IsoMessage>(new Exception("Connector Error")));

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }
}
