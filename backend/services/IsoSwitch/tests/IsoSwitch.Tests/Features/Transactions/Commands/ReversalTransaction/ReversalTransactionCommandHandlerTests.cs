using IsoSwitch.Application.Config;
using IsoSwitch.Application.Features.Transactions.Commands.ReversalTransaction;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Tests.Features.Transactions.Commands.ReversalTransaction;

public class ReversalTransactionCommandHandlerTests : IDisposable
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly IAcquirerConnector _connector;
    private readonly ReversalTransactionCommandHandler _sut;

    public ReversalTransactionCommandHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        
        _connector = Substitute.For<IAcquirerConnector>();
        _connector.ConnectorId.Returns("SIMULATOR");
        
        _registry = new ConnectorRegistry(new[] { _connector });
        _macSvc = Substitute.For<IMacService>();
        _publisher = Substitute.For<ISwitchEventPublisher>();
        _audit = Substitute.For<IIsoAuditService>();

        _sut = new ReversalTransactionCommandHandler(_db, _registry, _macSvc, _publisher, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_ExistingTransaction_ShouldReverseAndSave()
    {
        // Arrange
        var originalTraceId = "trace-original-001";
        var originalTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = originalTraceId,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            ConnectorId = "SIMULATOR",
            Stan = "123456",
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(originalTx);
        await _db.SaveChangesAsync();

        var command = new ReversalTransactionCommand(OriginalTraceId: originalTraceId);

        var responseIso = new IsoMessage { Mti = "0410" };
        responseIso.Set(39, "00");

        _connector.ReversalAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be("REVERSED");
        result.ReversalResponseCode.Should().Be("00");

        var updatedTx = await _db.Transactions.FirstOrDefaultAsync(t => t.TraceId == originalTraceId);
        updatedTx!.Status.Should().Be("REVERSED");
    }

    [Fact]
    public async Task Handle_AlreadyReversed_ShouldReturnCurrentStateWithoutReCall()
    {
        // Arrange
        var traceId = "already-reversed";
        var tx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = traceId,
            Status = "REVERSED",
            ResponseCode = "00",
            Decision = "REVERSED",
            ConnectorId = "SIMULATOR",
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        var command = new ReversalTransactionCommand(OriginalTraceId: traceId);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be("REVERSED");
        await _connector.DidNotReceive().ReversalAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransactionNotFound_ShouldThrow()
    {
        // Arrange
        var command = new ReversalTransactionCommand(OriginalTraceId: "non-existent");

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Original transaction not found");
    }
    [Fact]
    public async Task Handle_ConnectorThrows_ShouldMarkAsInDoubt()
    {
        // Arrange
        var traceId = "tr-reversal-doubt";
        var originalTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = traceId,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            ConnectorId = "SIMULATOR",
            Stan = "999888",
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(originalTx);
        await _db.SaveChangesAsync();

        _connector.ReversalAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IsoMessage>(new Exception("Connector Error")));

        var command = new ReversalTransactionCommand(traceId);

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == traceId);
        dbTx.Status.Should().Be(TransactionStatuses.InDoubt);
        dbTx.InDoubt.Should().BeTrue();
    }
}
