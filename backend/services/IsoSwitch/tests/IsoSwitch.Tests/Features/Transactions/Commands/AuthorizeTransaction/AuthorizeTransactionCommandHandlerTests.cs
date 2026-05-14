using IsoSwitch.Api.Features.Transactions.Commands.AuthorizeTransaction;
using IsoSwitch.Api.Services;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.Persistence.Transactions;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using IsoSwitch.Api;
using FluentAssertions;
using IsoSwitch.Tests.Infrastructure;

namespace IsoSwitch.Tests.Features.Transactions.Commands.AuthorizeTransaction;

public class AuthorizeTransactionCommandHandlerTests : IDisposable
{
    private readonly IsoSwitchDbContext _db;
    private readonly ConnectorRegistry _registry;
    private readonly IRoutingEngineV2 _routerV2;
    private readonly IMacService _macSvc;
    private readonly ISwitchEventPublisher _publisher;
    private readonly IIsoAuditService _audit;
    private readonly IAcquirerConnector _connector;
    private readonly AuthorizeTransactionCommandHandler _sut;

    public AuthorizeTransactionCommandHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        _connector = Substitute.For<IAcquirerConnector>();
        _connector.ConnectorId.Returns("TEST-CONN");
        
        _registry = new ConnectorRegistry(new[] { _connector });
        _routerV2 = Substitute.For<IRoutingEngineV2>();
        _macSvc = Substitute.For<IMacService>();
        _publisher = Substitute.For<ISwitchEventPublisher>();
        _audit = Substitute.For<IIsoAuditService>();

        _sut = new AuthorizeTransactionCommandHandler(_db, _registry, _routerV2, _macSvc, _publisher, _audit,
            NullLogger<AuthorizeTransactionCommandHandler>.Instance);

        // Default routing
        _routerV2.ResolveAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RoutingDecision("TEST-CONN", "ONLINE", null, null, null));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_ValidRequest_ShouldApproveAndSave()
    {
        // Arrange
        var command = CreateCommand("tr-001");
        var respIso = new IsoMessage { Mti = "0110" };
        respIso.Set(39, "00");
        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>()).Returns(respIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Confirmed);
        result.Decision.Should().Be("APPROVED");
        result.ResponseCode.Should().Be("00");

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == "tr-001");
        dbTx.Status.Should().Be(TransactionStatuses.Confirmed);
        dbTx.Amount12.Should().Be("10050");
    }

    [Fact]
    public async Task Handle_ConnectorDeclines_ShouldMarkAsDeclined()
    {
        // Arrange
        var command = CreateCommand("tr-002");
        var respIso = new IsoMessage { Mti = "0110" };
        respIso.Set(39, "05"); // Declined
        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>()).Returns(respIso);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(TransactionStatuses.Declined);
        result.Decision.Should().Be("DECLINED");
        result.ResponseCode.Should().Be("05");

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == "tr-002");
        dbTx.Status.Should().Be(TransactionStatuses.Declined);
    }

    [Fact]
    public async Task Handle_WithIdempotency_ShouldReturnExisting()
    {
        // Arrange
        var idemKey = "idem-123";
        _db.Transactions.Add(new TransactionEntity
        {
            TraceId = "tr-old",
            IdempotencyKey = idemKey,
            TxType = TransactionTypes.Auth,
            Status = TransactionStatuses.Confirmed,
            Decision = "APPROVED",
            ResponseCode = "00",
            ConnectorId = "TEST-CONN",
            RequestMti = "0100",
            Stan = "111",
            RequestJson = "{}"
        });
        await _db.SaveChangesAsync();

        var command = CreateCommand("tr-new", idemKey);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.TraceId.Should().Be("tr-old");
        await _connector.DidNotReceive().AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConnectorThrows_ShouldMarkAsInDoubt()
    {
        // Arrange
        var command = CreateCommand("tr-003");
        _connector.AuthorizeAsync(Arg.Any<IsoMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IsoMessage>(new Exception("Connector Error")));

        // Act
        var act = () => _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>();

        var dbTx = await _db.Transactions.FirstAsync(t => t.TraceId == "tr-003");
        dbTx.Status.Should().Be(TransactionStatuses.InDoubt);
        dbTx.InDoubt.Should().BeTrue();
    }

    private AuthorizeTransactionCommand CreateCommand(string traceId, string? idemKey = null)
    {
        return new AuthorizeTransactionCommand(
            TraceId: traceId,
            Bin: 123456,
            Amount: 100.50m,
            Currency: "840",
            MerchantId: "M001",
            TerminalId: "T001",
            Stan: "123456",
            PinBlock: null,
            EmvTlv: null,
            Pan: "1234123412341234",
            ExpiryYyMm: "2512",
            PosEntryMode: "051",
            PosConditionCode: "00",
            Track2: null,
            AdditionalAmounts54: null,
            Private60: null,
            Private61: null,
            Private62: null,
            IdempotencyKey: idemKey
        );
    }
}
