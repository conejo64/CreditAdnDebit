using System.Text.RegularExpressions;
using FluentAssertions;
using IsoSwitch.Application.Config;
using IsoSwitch.Application.Features.Transactions.Commands.AuthorizeTransaction;
using IsoSwitch.Application.Ports;
using IsoSwitch.Domain;
using IsoSwitch.Infrastructure.Messaging;
using IsoSwitch.Infrastructure.Persistence;
using IsoSwitch.Infrastructure.SwitchIso8583.Connectors;
using IsoSwitch.Infrastructure.SwitchIso8583.Iso;
using IsoSwitch.Infrastructure.SwitchIso8583.Routing;
using IsoSwitch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IsoSwitch.IntegrationTests.Persistence;

/// <summary>
/// End-to-end over the migrated schema: an authorization carrying a PAN, track 2 and a PIN block
/// runs through the real <see cref="AuthorizeTransactionCommandHandler"/> and the real
/// <see cref="IsoAuditService"/> against PostgreSQL. The connector must still receive the clear
/// fields, while the columns read back with raw SQL must hold no 16-digit run and no track 2
/// separator followed by digits.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait(Category.TraitName, Category.Integration)]
public sealed partial class ClearCardDataNeverPersistedTest
{
    private const string Pan = "4539578763621486";
    private const string Track2 = "4539578763621486=29121011234567890";
    private const string PinBlock = "A1B2C3D4E5F60718";
    private const string ConnectorId = "SIM-IT";

    private readonly PostgresFixture _fixture;

    public ClearCardDataNeverPersistedTest(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [GeneratedRegex(@"\d{16}")]
    private static partial Regex SixteenDigitRun();

    // Only the ASCII separator: the hex 'D' variant would collide with alphanumeric fields such as the RRN (DE37).
    [GeneratedRegex(@"=\d{4}")]
    private static partial Regex Track2SeparatorWithDigits();

    [Fact(DisplayName = "An authorization persists masked card data on PostgreSQL while the connector gets the clear fields")]
    public async Task Authorize_persists_masked_request_and_audit_rows()
    {
        var options = await _fixture.CreateOptionsAsync();
        await using var db = new IsoSwitchDbContext(options);
        await db.Database.MigrateAsync();

        var connector = new EchoingConnector();
        var registry = new ConnectorRegistry(new IAcquirerConnector[] { connector }, new ConfigurationBuilder().Build());
        var handler = new AuthorizeTransactionCommandHandler(
            db,
            registry,
            new FixedRouter(),
            new FixedHsm(),
            new NoopPublisher(),
            new IsoAuditService(db),
            NullLogger<AuthorizeTransactionCommandHandler>.Instance);

        var traceId = $"it-mask-{Guid.NewGuid():N}";
        var command = new AuthorizeTransactionCommand(
            TraceId: traceId,
            Bin: 453957,
            Amount: 100.50m,
            Currency: "840",
            MerchantId: "M001",
            TerminalId: "T001",
            Stan: "123456",
            PinBlock: PinBlock,
            EmvTlv: null,
            Pan: Pan,
            ExpiryYyMm: "2912",
            PosEntryMode: "051",
            PosConditionCode: "00",
            Track2: Track2,
            AdditionalAmounts54: null,
            Private60: null,
            Private61: null,
            Private62: null,
            IdempotencyKey: null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.ResponseCode.Should().Be("00");
        connector.Received.Should().NotBeNull();
        connector.Received!.Fields[2].Should().Be(Pan);
        connector.Received.Fields[35].Should().Be(Track2);
        connector.Received.Fields[52].Should().Be(PinBlock);

        // Read the columns back with raw SQL on a fresh connection: what PostgreSQL holds, not the tracker.
        await using var reader = PostgresFixture.CreateSiblingContext(db);

        var requestJson = await reader.Database
            .SqlQuery<string>($"SELECT \"RequestJson\" AS \"Value\" FROM \"Transactions\" WHERE \"TraceId\" = {traceId}")
            .SingleAsync();
        var responseJson = await reader.Database
            .SqlQuery<string?>($"SELECT \"ResponseJson\" AS \"Value\" FROM \"Transactions\" WHERE \"TraceId\" = {traceId}")
            .SingleAsync();
        var auditRows = await reader.Database
            .SqlQuery<string>($"SELECT \"FieldsJson\" AS \"Value\" FROM \"iso_message_logs\" WHERE \"TraceId\" = {traceId}")
            .ToListAsync();

        auditRows.Should().HaveCount(2, "one OUT and one IN audit row");

        foreach (var stored in auditRows.Append(requestJson).Append(responseJson!))
        {
            stored.Should().NotBeNull();
            stored.Should().NotContain(Pan).And.NotContain(Track2).And.NotContain(PinBlock);
            SixteenDigitRun().IsMatch(stored).Should().BeFalse("a 16-digit run would be a PAN: {0}", stored);
            Track2SeparatorWithDigits().IsMatch(stored).Should().BeFalse("a track 2 separator followed by digits would be an expiry: {0}", stored);
        }

        requestJson.Should().Contain(CardDataMasking.MaskPan(Pan));
        requestJson.Should().Contain("10050");
        responseJson.Should().Contain("\"39\":\"00\"");
    }

    private sealed class EchoingConnector : IAcquirerConnector
    {
        public IsoMessage? Received { get; private set; }
        public string ConnectorId => ClearCardDataNeverPersistedTest.ConnectorId;

        public Task<IsoMessage> AuthorizeAsync(IsoMessage request, CancellationToken ct)
        {
            Received = request;
            var response = new IsoMessage { Mti = "0110" };
            // Acquirers commonly echo DE2 back; it must be masked on the IN audit row too.
            if (request.Fields.TryGetValue(2, out var pan)) response.Set(2, pan);
            response.Set(39, "00");
            return Task.FromResult(response);
        }

        public Task<IsoMessage> ReversalAsync(IsoMessage request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedRouter : IRoutingEngineV2
    {
        public Task<RoutingDecision> ResolveAsync(int bin, string? countryCode, string? network, string txType, CancellationToken ct)
            => Task.FromResult(new RoutingDecision(ConnectorId, "ONLINE", null, null, null));
    }

    private sealed class FixedHsm : IHsmService
    {
        public string ComputeMacHex(string payloadAscii) => "0011223344556677";
        public bool TryParsePinBlock(string encryptedPinBlock, string accountInfo, out string clearPin)
        {
            clearPin = string.Empty;
            return false;
        }
    }

    private sealed class NoopPublisher : ISwitchEventPublisher
    {
        public Task PublishTxAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task PublishIsoAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task PublishAuditAsync(string key, object payload, CancellationToken ct) => Task.CompletedTask;
    }
}
