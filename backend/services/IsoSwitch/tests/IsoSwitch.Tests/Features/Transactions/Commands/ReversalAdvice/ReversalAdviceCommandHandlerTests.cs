using IsoSwitch.Application;
using IsoSwitch.Application.Config;
using IsoSwitch.Application.Features.Transactions.Commands.ReversalAdvice;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IsoSwitch.Tests.Features.Transactions.Commands.ReversalAdvice;


public class ReversalAdviceCommandHandlerTests : IDisposable
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly Field90Service _field90Svc;
    private readonly IMacService _macSvc;
    private readonly IAcquirerConnector _connector;
    private readonly ReversalAdviceCommandHandler _sut;

    public ReversalAdviceCommandHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        
        _connector = Substitute.For<IAcquirerConnector>();
        _connector.ConnectorId.Returns("SIMULATOR");
        
        _registry = new ConnectorRegistry(new[] { _connector });
        _publisher = Substitute.For<ISwitchEventPublisher>();
        _audit = Substitute.For<IIsoAuditService>();
        
        var cfg = Substitute.For<IConfiguration>();
        _field90Svc = new Field90Service(cfg);
        
        _macSvc = Substitute.For<IMacService>();

        _sut = new ReversalAdviceCommandHandler(_db, _registry, _publisher, _audit, _field90Svc, _macSvc);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_ValidAdvice_ShouldApproveAndUpdateOriginal()
    {
        // Arrange
        var originalTraceId = "trace-original-rev";
        var originalTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = originalTraceId,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            ConnectorId = "SIMULATOR",
            Stan = "111222",
            CreatedOn = DateTimeOffset.UtcNow,
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(originalTx);
        await _db.SaveChangesAsync();

        var command = new ReversalAdviceCommand(
            TraceId: "advice-trace-001",
            OriginalTraceId: originalTraceId
        );

        var responseIso = new IsoMessage { Mti = "0430" };
        responseIso.Set(39, "00"); // Approved

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Confirmed);
        result.Decision.Should().Be("APPROVED");
        result.Field90.Should().NotBeNullOrWhiteSpace();

        // Check original TX state
        var updatedOriginal = await _db.Transactions.FirstAsync(t => t.TraceId == originalTraceId);
        updatedOriginal.ReversalState.Should().Be("REVERSAL_CONFIRMED");
        updatedOriginal.ReversalConfirmedOn.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FailedAdvice_ShouldMarkAsReversalFailed()
    {
        // Arrange
        var originalTraceId = "trace-failed-rev";
        var originalTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = originalTraceId,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            ConnectorId = "SIMULATOR",
            CreatedOn = DateTimeOffset.UtcNow,
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(originalTx);
        await _db.SaveChangesAsync();

        var command = new ReversalAdviceCommand(
            TraceId: "advice-trace-002",
            OriginalTraceId: originalTraceId
        );

        var responseIso = new IsoMessage { Mti = "0430" };
        responseIso.Set(39, "05"); // Declined

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(responseIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Declined);
        
        var updatedOriginal = await _db.Transactions.FirstAsync(t => t.TraceId == originalTraceId);
        updatedOriginal.ReversalState.Should().Be("REVERSAL_FAILED");
    }

    [Fact]
    public async Task Handle_Idempotency_ShouldReturnExistingResult()
    {
        // Arrange
        var idempoKey = "rev-idem-456";
        var existingTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = "existing-rev-trace",
            IdempotencyKey = idempoKey,
            TxType = TransactionTypes.ReversalAdvice,
            Status = TransactionStatuses.Confirmed,
            Decision = "APPROVED",
            ResponseCode = "00",
            ConnectorId = "SIMULATOR",
            RequestMti = "0420",
            RequestJson = "{}"
        };
        _db.Transactions.Add(existingTx);
        await _db.SaveChangesAsync();

        var command = new ReversalAdviceCommand("new-trace", "orig-trace", idempoKey);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.TraceId.Should().Be("existing-rev-trace");
        await _connector.DidNotReceive().AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Handle_ConnectorThrows_ShouldMarkAsInDoubt()
    {
        // Arrange
        var originalTraceId = "trace-original-adv-doubt";
        var originalTx = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            TraceId = originalTraceId,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            ConnectorId = "SIMULATOR",
            CreatedOn = DateTimeOffset.UtcNow,
            RequestMti = "0100",
            RequestJson = "{}"
        };
        _db.Transactions.Add(originalTx);
        await _db.SaveChangesAsync();

        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IsoMessage>(new Exception("Connector Error")));

        var command = new ReversalAdviceCommand("advice-trace-doubt", originalTraceId);

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == "advice-trace-doubt");
        dbTx.Status.Should().Be(TransactionStatuses.InDoubt);
        dbTx.InDoubt.Should().BeTrue();
    }
}
