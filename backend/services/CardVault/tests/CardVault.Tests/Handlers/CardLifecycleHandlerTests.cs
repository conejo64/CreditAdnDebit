using CardVault.Api.Features.Issuer.Commands;
using CardVault.Api.Services;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CardVault.Tests.Handlers;

/// <summary>
/// TDD unit tests for UnblockCard, CancelCard, and ReplaceCard command handlers.
/// RED: written before the three commands and their handlers exist in IssuerCommands.cs.
/// </summary>
public sealed class CardLifecycleHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly IssuerService _issuerService;
    private readonly AuditService _auditService;

    public CardLifecycleHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _auditService = new AuditService(_db);
        _issuerService = new IssuerService(_db, _auditService);
    }

    public void Dispose() => _db.Dispose();

    // ─────────────────────────────────────────────
    // UnblockCardCommand
    // ─────────────────────────────────────────────

    [Fact]
    public async Task UnblockCardCommand_BlockedCard_ShouldReturnNoContent()
    {
        // Arrange
        var card = await CreateBlockedCard();
        var handler = new UnblockCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new UnblockCardCommand(card.Id), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task UnblockCardCommand_NonExistingCard_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new UnblockCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new UnblockCardCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UnblockCardCommand_ActiveCard_ShouldReturnConflict()
    {
        // Arrange — card is Active (not Blocked), cannot be unblocked
        var card = await CreateActiveCard();
        var handler = new UnblockCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new UnblockCardCommand(card.Id), CancellationToken.None);

        // Assert — 409 Conflict: status transition is invalid
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(409);
    }

    // ─────────────────────────────────────────────
    // CancelCardCommand
    // ─────────────────────────────────────────────

    [Fact]
    public async Task CancelCardCommand_ActiveCard_ShouldReturnNoContent()
    {
        // Arrange
        var card = await CreateActiveCard();
        var handler = new CancelCardCommandHandler(_issuerService);
        var request = new CancelCardRequest("client request");

        // Act
        var result = await handler.Handle(new CancelCardCommand(card.Id, request), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task CancelCardCommand_NonExistingCard_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new CancelCardCommandHandler(_issuerService);
        var request = new CancelCardRequest(null);

        // Act
        var result = await handler.Handle(new CancelCardCommand(Guid.NewGuid(), request), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CancelCardCommand_AlreadyCancelledCard_ShouldReturnConflict()
    {
        // Arrange
        var card = await CreateActiveCard();
        await _issuerService.ChangeStatusAsync(card.Id, CardStatus.Cancelled, "pre-cancel", CancellationToken.None);
        var handler = new CancelCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new CancelCardCommand(card.Id, new CancelCardRequest(null)), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(409);
    }

    // ─────────────────────────────────────────────
    // ReplaceCardCommand
    // ─────────────────────────────────────────────

    [Fact]
    public async Task ReplaceCardCommand_ActiveCard_ShouldReturnCreated()
    {
        // Arrange
        var card = await CreateActiveCard();
        var handler = new ReplaceCardCommandHandler(_issuerService);
        var request = new ReplaceCardRequest("damaged");

        // Act
        var result = await handler.Handle(new ReplaceCardCommand(card.Id, request), CancellationToken.None);

        // Assert — 201 Created with new card data
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task ReplaceCardCommand_NonExistingCard_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new ReplaceCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ReplaceCardCommand(Guid.NewGuid(), new ReplaceCardRequest(null)), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ReplaceCardCommand_CancelledCard_ShouldReturnConflict()
    {
        // Arrange — cannot replace an already-cancelled card
        var card = await CreateActiveCard();
        await _issuerService.ChangeStatusAsync(card.Id, CardStatus.Cancelled, "pre-cancel", CancellationToken.None);
        var handler = new ReplaceCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ReplaceCardCommand(card.Id, new ReplaceCardRequest(null)), CancellationToken.None);

        // Assert
        var statusResult = result as IStatusCodeHttpResult;
        statusResult?.StatusCode.Should().Be(409);
    }

    // ─────────────────────────────────────────────
    // GAP-5: ReplaceCard response body must use { newCardId }
    // ─────────────────────────────────────────────

    /// <summary>
    /// GAP-5a (RED): Spec HC-2-S3 requires the replace response body to be
    /// { "newCardId": "..." }, NOT { "id": "..." }.
    /// This test fails until IssuerCommands.ReplaceCardCommandHandler uses
    /// new { newCardId = newCard!.Id }.
    /// </summary>
    [Fact]
    public async Task ReplaceCardCommand_ResponseBody_ShouldUseNewCardIdKey()
    {
        // Arrange
        var card = await CreateActiveCard();
        var handler = new ReplaceCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ReplaceCardCommand(card.Id, new ReplaceCardRequest("damaged")), CancellationToken.None);

        // Assert — 201 with body { newCardId }, not { id }
        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        var valueResult = result as IValueHttpResult;
        valueResult.Should().NotBeNull("Created result must carry a body");

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(valueResult!.Value, opts);
        json.Should().Contain("\"newCardId\"",
            because: "spec HC-2-S3 requires response key 'newCardId'");
        // The old multi-field shape must not appear
        json.Should().NotContain("\"accountId\"",
            because: "replace response must only expose newCardId, not the full card payload");
    }

    /// <summary>
    /// GAP-5b (RED): Spec ILB-CL-2-S1 requires bidirectional audit linkage:
    ///   - old card history must contain the new card id (successor reference)
    ///   - new card history must contain the old card id (predecessor reference)
    /// This test fails until IssuerService.ReplaceCardAsync writes both entries.
    /// </summary>
    [Fact]
    public async Task ReplaceCardCommand_ShouldWriteBidirectionalHistoryLinkage()
    {
        // Arrange
        var card = await CreateActiveCard();
        var handler = new ReplaceCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ReplaceCardCommand(card.Id, new ReplaceCardRequest("lost")), CancellationToken.None);

        // Extract the new card id from the response body
        var valueResult = result as IValueHttpResult;
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(valueResult!.Value, opts);
        var doc = JsonDocument.Parse(json);
        var newCardId = doc.RootElement.GetProperty("newCardId").GetString()!;
        var newCardGuid = Guid.Parse(newCardId);

        // Old card history: at least one entry whose Reason encodes the new card id
        var oldHistory = await _db.CardStatusHistory
            .Where(h => h.CardId == card.Id)
            .ToListAsync();
        oldHistory.Should().Contain(h => h.Reason.Contains(newCardId),
            because: "spec ILB-CL-2-S1: old card history must reference the successor card id");

        // New card history: at least one entry whose Reason encodes the old card id
        var newHistory = await _db.CardStatusHistory
            .Where(h => h.CardId == newCardGuid)
            .ToListAsync();
        newHistory.Should().Contain(h => h.Reason.Contains(card.Id.ToString()),
            because: "spec ILB-CL-2-S1: new card history must reference the predecessor card id");
    }

    // ─────────────────────────────────────────────
    // GAP-4: BlockCard must emit named audit event
    // ─────────────────────────────────────────────

    /// <summary>
    /// GAP-4 (RED): Spec requires BlockCardCommand to emit a named 'issuer.card.blocked'
    /// audit event in addition to the generic 'issuer.card.status_changed'.
    /// Fails until BlockCardCommandHandler delegates to BlockCardAsync (new IssuerService method).
    /// </summary>
    [Fact]
    public async Task BlockCardCommand_ShouldEmitNamedBlockedAuditEvent()
    {
        // Arrange
        var card = await CreateActiveCard();
        var handler = new BlockCardCommandHandler(_issuerService);

        // Act
        await handler.Handle(new BlockCardCommand(card.Id, new BlockCardRequest("fraud")), CancellationToken.None);

        // Assert — named event must be present in the audit log
        var audits = await _auditService.LatestAsync(50, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.card.blocked",
            because: "spec requires a named 'issuer.card.blocked' audit event for card blocking");
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private async Task<CardEntity> CreateActiveCard()
    {
        var customer = await new CustomerService(_db).CreateAsync(
            "Test Cust", $"LC{Guid.NewGuid():N}"[..10], "lc@t.com", "111",
            "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        var account = await _issuerService.CreateAccountAsync(
            customer.Id, AccountType.Credit, "VISA", 5000m, CancellationToken.None);
        var card = await _issuerService.IssueCardAsync(
            account.Id, "411111", "4111111111111111", "2812", CancellationToken.None);
        await _issuerService.ChangeStatusAsync(card.Id, CardStatus.Active, "activated", CancellationToken.None);
        return card;
    }

    private async Task<CardEntity> CreateBlockedCard()
    {
        var card = await CreateActiveCard();
        await _issuerService.ChangeStatusAsync(card.Id, CardStatus.Blocked, "fraud suspicion", CancellationToken.None);
        return card;
    }
}
