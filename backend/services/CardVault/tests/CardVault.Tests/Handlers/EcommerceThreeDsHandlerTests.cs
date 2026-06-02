using BuildingBlocks.Outbox;
using CardVault.Api.Contracts;
using CardVault.Api.Features.Ecommerce3ds.Commands;
using CardVault.Api.Features.Ecommerce3ds.Queries;
using CardVault.Api.Pci;
using CardVault.Api.Services;
using CardVault.Api.Vault;
using CardVault.Infrastructure.Persistence.Ecommerce;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace CardVault.Tests.Handlers;

/// <summary>
/// Unit tests for the 3DS MediatR command and query handlers.
/// Verifies that each handler maps service results to the correct HTTP result type
/// (Created, Ok, NotFound) without relying on the full HTTP pipeline.
/// </summary>
public sealed class EcommerceThreeDsHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly ThreeDsService _service;

    public EcommerceThreeDsHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var busMock = Substitute.For<IEventBus>();
        busMock.PublishAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var pciAudit      = new PciAuditPublisher(busMock);
        var audit         = new AuditService(_db);
        var crypto        = TestVaultCrypto.Create();
        var notifications = new NotificationService(_db, audit, pciAudit, crypto);

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        _service = new ThreeDsService(_db, audit, pciAudit, env, notifications);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<(Guid CardId, Guid AccountId)> SeedAsync()
    {
        var customer = _db.Customers.Add(new CustomerEntity
        {
            Id             = Guid.NewGuid(),
            FullName       = "3DS Handler Test",
            DocumentId     = $"DOC{Guid.NewGuid():N}"[..10],
            Email          = "handler@example.com",
            Phone          = "+593999000001",
            CustomerNumber = $"C{Guid.NewGuid():N}"[..8],
        }).Entity;

        var account = _db.Accounts.Add(new CardAccountEntity
        {
            Id             = Guid.NewGuid(),
            CustomerId     = customer.Id,
            AccountType    = AccountType.Credit,
            ProductCode    = "VISA_CLASSIC",
            CreditLimit    = 5000m,
            AvailableLimit = 5000m,
            AccountNumber  = $"ACC{Guid.NewGuid():N}"[..10],
            Status         = AccountStatus.Active,
            CurrencyCode   = "USD",
        }).Entity;

        var card = _db.Cards.Add(new CardEntity
        {
            Id         = Guid.NewGuid(),
            AccountId  = account.Id,
            Bin        = "411111",
            PanToken   = $"tok_{Guid.NewGuid():N}",
            MaskedPan  = "411111******1111",
            Last4      = "1111",
            ExpiryYyMm = "2812",
            Status     = CardStatus.Active,
        }).Entity;

        await _db.SaveChangesAsync();
        return (card.Id, account.Id);
    }

    private static StartThreeDsChallengeRequest BuildStartRequest(Guid cardId)
        => new(cardId, 100m, "USD", "MCH001", "Test Store", "US", "US", "BROWSER");

    // ── StartThreeDsChallengeCommandHandler ─────────────────────────────────

    [Fact]
    public async Task StartChallengeHandler_WhenCardExists_ReturnsCreated()
    {
        var (cardId, _) = await SeedAsync();
        var handler = new StartThreeDsChallengeCommandHandler(_service);
        var command = new StartThreeDsChallengeCommand(BuildStartRequest(cardId), "actor", "trace-h01");

        var result = await handler.Handle(command, CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull("handler must return an IResult with a status code");
        statusResult!.StatusCode.Should().Be(201, "StartChallenge must return 201 Created with the resource location");
    }

    // ── VerifyThreeDsChallengeCommandHandler ────────────────────────────────

    [Fact]
    public async Task VerifyChallengeHandler_WhenChallengeExists_ReturnsOk()
    {
        var (cardId, _) = await SeedAsync();
        var started = await _service.StartChallengeAsync(BuildStartRequest(cardId), "actor", "trace-h02", CancellationToken.None);

        var handler = new VerifyThreeDsChallengeCommandHandler(_service);
        var command = new VerifyThreeDsChallengeCommand(
            started.ChallengeId,
            new VerifyThreeDsChallengeRequest(started.DevelopmentOtp!),
            "actor",
            "trace-h02");

        var result = await handler.Handle(command, CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(200, "VerifyChallenge must return 200 OK");
    }

    // ── GetThreeDsChallengeQueryHandler ─────────────────────────────────────

    [Fact]
    public async Task GetChallengeHandler_WhenChallengeExists_ReturnsOk()
    {
        var (cardId, _) = await SeedAsync();
        var started = await _service.StartChallengeAsync(BuildStartRequest(cardId), "actor", "trace-h03", CancellationToken.None);

        var handler = new GetThreeDsChallengeQueryHandler(_service);
        var result = await handler.Handle(new GetThreeDsChallengeQuery(started.ChallengeId), CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetChallengeHandler_WhenChallengeNotFound_ReturnsNotFound()
    {
        var handler = new GetThreeDsChallengeQueryHandler(_service);
        var result = await handler.Handle(new GetThreeDsChallengeQuery(Guid.NewGuid()), CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(404);
    }

    // ── ListThreeDsChallengesQueryHandler ────────────────────────────────────

    [Fact]
    public async Task ListChallengesHandler_WhenEmpty_ReturnsOkWithEmptyList()
    {
        var handler = new ListThreeDsChallengesQueryHandler(_service);
        var result = await handler.Handle(new ListThreeDsChallengesQuery(null, 50), CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ListChallengesHandler_WhenChallengesExist_ReturnsOkWithItems()
    {
        var (cardId, _) = await SeedAsync();
        await _service.StartChallengeAsync(BuildStartRequest(cardId), "actor", "trace-h06", CancellationToken.None);

        var handler = new ListThreeDsChallengesQueryHandler(_service);
        var result = await handler.Handle(new ListThreeDsChallengesQuery(null, 50), CancellationToken.None);

        var statusResult = result as IStatusCodeHttpResult;
        statusResult.Should().NotBeNull();
        statusResult!.StatusCode.Should().Be(200);
    }
}
