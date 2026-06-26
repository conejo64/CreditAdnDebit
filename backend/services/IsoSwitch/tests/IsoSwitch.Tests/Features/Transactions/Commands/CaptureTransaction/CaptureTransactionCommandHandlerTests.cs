using IsoSwitch.Application.Config;
using IsoSwitch.Application.Features.Transactions.Commands.CaptureTransaction;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using IsoSwitch.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;

namespace IsoSwitch.Tests.Features.Transactions.Commands.CaptureTransaction;

public class CaptureTransactionCommandHandlerTests : IDisposable
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IRoutingEngineV2 _router;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly IAcquirerConnector _connector;
    private readonly CaptureTransactionCommandHandler _sut;

    public CaptureTransactionCommandHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        
        _connector = Substitute.For<IAcquirerConnector>();
        _connector.ConnectorId.Returns("SIMULATOR");
        
        _registry = new ConnectorRegistry(new[] { _connector });
        _router = Substitute.For<IRoutingEngineV2>();
        _macSvc = Substitute.For<IMacService>();
        _publisher = Substitute.For<ISwitchEventPublisher>();
        _audit = Substitute.For<IIsoAuditService>();

        _sut = new CaptureTransactionCommandHandler(_db, _registry, _router, _macSvc, _publisher, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_ValidCapture_ShouldApproveAndSave()
    {
        // Arrange
        var traceId = Guid.NewGuid().ToString("N");
        var command = new CaptureTransactionCommand(
            TraceId: traceId,
            Bin: 411111,
            Amount: 100m,
            Currency: "840",
            MerchantId: "M01",
            TerminalId: "T01",
            Stan: "000001",
            Pan: "4111111111111111"
        );

        _router.ResolveAsync(411111, null, null, "AUTH", Arg.Any<CancellationToken>())
            .Returns(new RoutingDecision("SIMULATOR", "STATIC", Guid.NewGuid(), null, null));

        var responseIso = new IsoMessage { Mti = "0210" };
        responseIso.Set(39, "00"); // Approved

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Captured);
        result.Decision.Should().Be("APPROVED");
        result.ResponseCode.Should().Be("00");

        var savedTx = await _db.Transactions.FirstOrDefaultAsync(t => t.TraceId == traceId);
        savedTx.Should().NotBeNull();
        savedTx!.Status.Should().Be(TransactionStatuses.Captured);
        savedTx.Amount12.Should().Be("10000"); // 100 * 100
    }

    [Fact]
    public async Task Handle_DeclinedCapture_ShouldSaveAsDeclined()
    {
        // Arrange
        var traceId = Guid.NewGuid().ToString("N");
        var command = new CaptureTransactionCommand(traceId, 411111, 100m, "840", "M01", "T01", "000002");

        _router.ResolveAsync(411111, null, null, "AUTH", Arg.Any<CancellationToken>())
            .Returns(new RoutingDecision("SIMULATOR", "STATIC", Guid.NewGuid(), null, null));

        var responseIso = new IsoMessage { Mti = "0210" };
        responseIso.Set(39, "05"); // Declined

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Declined);
        result.Decision.Should().Be("DECLINED");
        result.ResponseCode.Should().Be("05");
    }

    [Fact]
    public async Task Handle_WithIdempotencyKey_ShouldReturnExistingResult()
    {
        // Arrange
        var idempotencyKey = "idem-123";
        var existingTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = "original-trace",
            IdempotencyKey = idempotencyKey,
            TxType = TransactionTypes.Capture,
            Status = TransactionStatuses.Captured,
            Decision = "APPROVED",
            ResponseCode = "00",
            ConnectorId = "SIMULATOR",
            RequestMti = "0200",
            RequestJson = "{}" // Required field
        };
        _db.Transactions.Add(existingTx);
        await _db.SaveChangesAsync();

        var command = new CaptureTransactionCommand(
            TraceId: "new-trace",
            Bin: 411111,
            Amount: 100m,
            Currency: "840",
            MerchantId: "M01",
            TerminalId: "T01",
            Stan: "000003",
            IdempotencyKey: idempotencyKey
        );

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.TraceId.Should().Be("original-trace");
        result.Status.Should().Be(TransactionStatuses.Captured);
        
        // Ensure no new transaction was added
        var count = await _db.Transactions.CountAsync();
        count.Should().Be(1);

        // Ensure connector was NOT called
        await _connector.DidNotReceive().AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Handle_ConnectorThrows_ShouldMarkAsInDoubt()
    {
        // Arrange
        var traceId = "tr-capture-doubt";
        var command = new CaptureTransactionCommand(traceId, 411111, 100m, "840", "M01", "T01", "000004");

        _router.ResolveAsync(411111, null, null, "AUTH", Arg.Any<CancellationToken>())
            .Returns(new RoutingDecision("SIMULATOR", "STATIC", Guid.NewGuid(), null, null));

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IsoMessage>(new Exception("Connector Error")));

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == traceId);
        dbTx.Status.Should().Be(TransactionStatuses.InDoubt);
        dbTx.InDoubt.Should().BeTrue();
    }
}
