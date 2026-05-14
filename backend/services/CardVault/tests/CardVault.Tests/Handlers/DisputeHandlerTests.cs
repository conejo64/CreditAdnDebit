using CardVault.Api.Features.Disputes.Commands;
using CardVault.Api.Services;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Switch;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using DisputeCaseEntity = CardVault.Infrastructure.Persistence.Billing.DisputeCaseEntity;
using DisputeStatus = CardVault.Infrastructure.Persistence.Billing.DisputeStatus;

namespace CardVault.Tests.Handlers;

public sealed class DisputeHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly DisputesService _disputesService;
    private readonly AuditService _auditService;

    public DisputeHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _auditService = new AuditService(_db);
        _disputesService = new DisputesService(_db, _auditService);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task TransitionDisputeCommand_ShouldUpdateStatusAndAddEvent()
    {
        // Arrange
        var dispute = new DisputeCaseEntity
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Rrn = "123456789012",
            Status = DisputeStatus.Open,
            OpenedOn = DateTimeOffset.UtcNow
        };
        _db.DisputeCases.Add(dispute);
        await _db.SaveChangesAsync();

        var handler = new TransitionDisputeCommandHandler(_disputesService);
        var request = new DisputeTransitionRequest("representment", "Test notes");

        // Act
        var result = await handler.Handle(new TransitionDisputeCommand(dispute.Id, request), CancellationToken.None);

        // Assert
        result.Should().BeOfType<Ok<DisputeCaseEntity>>();
        var okResult = result as Ok<DisputeCaseEntity>;
        okResult!.Value!.Status.Should().Be(DisputeStatus.Representment);

        // Verify event was saved
        var hasEvent = _db.DisputeEvents.Any(e => e.DisputeId == dispute.Id && e.Action == "representment");
        hasEvent.Should().BeTrue();

        // Verify audit log
        var audit = _db.AuditEvents.FirstOrDefault(e => e.EventType == "dispute.v1.transitioned");
        audit.Should().NotBeNull();
        audit!.PayloadJson.Should().Contain(dispute.Id.ToString());
    }

    [Fact]
    public async Task CloseDisputeCommand_Won_ShouldUpdateStatusAndResolvedDate()
    {
        // Arrange
        var dispute = new DisputeCaseEntity
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Rrn = "987654321098",
            Status = DisputeStatus.Representment,
            OpenedOn = DateTimeOffset.UtcNow
        };
        _db.DisputeCases.Add(dispute);
        await _db.SaveChangesAsync();

        var handler = new CloseDisputeCommandHandler(_disputesService);

        // Act
        var result = await handler.Handle(new CloseDisputeCommand(dispute.Id, true), CancellationToken.None);

        // Assert
        result.Should().BeOfType<Ok<DisputeCaseEntity>>();
        var okResult = result as Ok<DisputeCaseEntity>;
        okResult!.Value!.Status.Should().Be(DisputeStatus.Won);
        okResult.Value.ResolvedOn.Should().NotBeNull();
    }

    [Fact]
    public async Task CloseDisputeCommand_Lost_ShouldUpdateStatusToLost()
    {
        // Arrange
        var dispute = new DisputeCaseEntity
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Rrn = "111222333444",
            Status = DisputeStatus.Arbitration,
            OpenedOn = DateTimeOffset.UtcNow
        };
        _db.DisputeCases.Add(dispute);
        await _db.SaveChangesAsync();

        var handler = new CloseDisputeCommandHandler(_disputesService);

        // Act
        var result = await handler.Handle(new CloseDisputeCommand(dispute.Id, false), CancellationToken.None);

        // Assert
        result.Should().BeOfType<Ok<DisputeCaseEntity>>();
        var okResult = result as Ok<DisputeCaseEntity>;
        okResult!.Value!.Status.Should().Be(DisputeStatus.Lost);
    }
}
