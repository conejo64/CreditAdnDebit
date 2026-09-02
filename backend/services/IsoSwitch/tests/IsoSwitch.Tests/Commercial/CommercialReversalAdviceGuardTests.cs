using FluentAssertions;
using IsoSwitch.Application.Config;
using IsoSwitch.Application.Features.Transactions.Commands.ReversalAdvice;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Application;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace IsoSwitch.Tests.Commercial;

public class CommercialReversalAdviceGuardTests : IDisposable
{
    private readonly IsoSwitchDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_MissingOriginalTransaction_DoesNotCreateSimulatorBackedAdvice()
    {
        var connector = Substitute.For<IsoSwitch.Infrastructure.SwitchIso8583.Connectors.IAcquirerConnector>();
        connector.ConnectorId.Returns("SIMULATOR");
        var registry = new ConnectorRegistry([connector], new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var publisher = Substitute.For<ISwitchEventPublisher>();
        var audit = Substitute.For<IIsoAuditService>();
        var hsm = Substitute.For<IHsmService>();
        var field90Service = new Field90Service(new ConfigurationBuilder().Build());
        var sut = new ReversalAdviceCommandHandler(_db, registry, publisher, audit, field90Service, hsm);

        var act = () => sut.Handle(new ReversalAdviceCommand("rev-001", "missing-original"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Original transaction not found*");
        _db.Transactions.Should().BeEmpty();
        await publisher.DidNotReceive().PublishIsoAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await publisher.DidNotReceive().PublishTxAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}


