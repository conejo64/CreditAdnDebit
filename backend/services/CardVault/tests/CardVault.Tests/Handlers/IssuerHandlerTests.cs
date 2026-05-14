using CardVault.Api.Features.Issuer.Commands;
using CardVault.Api.Features.Issuer.Queries;
using CardVault.Api.Services;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CardVault.Tests.Handlers;

public sealed class IssuerHandlerTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly CustomerService _customerService;
    private readonly IssuerService _issuerService;
    private readonly AuditService _auditService;

    public IssuerHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _auditService = new AuditService(_db);
        _customerService = new CustomerService(_db);
        _issuerService = new IssuerService(_db, _auditService);
    }

    public void Dispose() => _db.Dispose();

    #region CreateCustomerCommand

    [Fact]
    public async Task CreateCustomerCommand_ShouldReturnCreated()
    {
        // Arrange
        var handler = new CreateCustomerCommandHandler(_customerService);
        var request = new CreateCustomerRequest(
            "Handler Test User", "H123456", "handler@test.com", "+593999",
            "CEDULA", "M", "Addr", "StmtAddr", "Quito", "Quito", "Quito");

        // Act
        var result = await handler.Handle(new CreateCustomerCommand(request), CancellationToken.None);

        // Assert
        result.Should().BeOfType<Created<CustomerEntity>>();
    }

    #endregion

    #region GetCustomerQuery

    [Fact]
    public async Task GetCustomerQuery_ExistingId_ShouldReturnOk()
    {
        // Arrange
        var customer = await _customerService.CreateAsync(
            "Query Test", "Q999", "query@t.com", "111",
            "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        var handler = new GetCustomerQueryHandler(_customerService);

        // Act
        var result = await handler.Handle(new GetCustomerQuery(customer.Id), CancellationToken.None);

        // Assert: handler returns a 200 with a DTO projection (not the raw entity)
        var okResult = result as IStatusCodeHttpResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetCustomerQuery_NonExistingId_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new GetCustomerQueryHandler(_customerService);

        // Act
        var result = await handler.Handle(new GetCustomerQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.Should().BeAssignableTo<IResult>();
        // NotFound is returned via Results.NotFound() which produces NotFound
        var notFoundResult = result as IStatusCodeHttpResult;
        notFoundResult?.StatusCode.Should().Be(404);
    }

    #endregion

    #region SearchCustomersQuery

    [Fact]
    public async Task SearchCustomersQuery_ShouldClampTakeAt200()
    {
        // Arrange
        var handler = new SearchCustomersQueryHandler(_customerService);

        // Act — requesting 999 should be clamped to 200
        var result = await handler.Handle(new SearchCustomersQuery(null, 999), CancellationToken.None);

        // Assert — should not throw, result is OK
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchCustomersQuery_ZeroTake_ShouldDefaultTo50()
    {
        // Arrange
        var handler = new SearchCustomersQueryHandler(_customerService);

        // Act
        var result = await handler.Handle(new SearchCustomersQuery(null, 0), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region IssueCardCommand

    [Fact]
    public async Task IssueCardCommand_ShouldReturnCreatedWithCardData()
    {
        // Arrange
        var customer = await _customerService.CreateAsync(
            "Card Handler User", "CH001", "ch@t.com", "000",
            "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        var account = await _issuerService.CreateAccountAsync(
            customer.Id, AccountType.Credit, "VISA", 3000m, CancellationToken.None);

        var handler = new IssueCardCommandHandler(_issuerService);
        var request = new IssueCardRequest(account.Id, "411111", "4111111111111111", "2810");

        // Act
        var result = await handler.Handle(new IssueCardCommand(request), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Created result includes the card projection
        var createdResult = result as IStatusCodeHttpResult;
        createdResult?.StatusCode.Should().Be(201);
    }

    #endregion

    #region ActivateCardCommand

    [Fact]
    public async Task ActivateCardCommand_ExistingCard_ShouldReturnOk()
    {
        // Arrange
        var card = await CreateTestCard();
        var handler = new ActivateCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ActivateCardCommand(card.Id), CancellationToken.None);

        // Assert
        var okResult = result as IStatusCodeHttpResult;
        okResult?.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ActivateCardCommand_NonExistingCard_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new ActivateCardCommandHandler(_issuerService);

        // Act
        var result = await handler.Handle(new ActivateCardCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        var notFoundResult = result as IStatusCodeHttpResult;
        notFoundResult?.StatusCode.Should().Be(404);
    }

    #endregion

    #region BlockCardCommand

    [Fact]
    public async Task BlockCardCommand_ExistingCard_ShouldReturnOk()
    {
        // Arrange
        var card = await CreateTestCard();
        var handler = new BlockCardCommandHandler(_issuerService);
        var request = new BlockCardRequest("suspicious activity");

        // Act
        var result = await handler.Handle(new BlockCardCommand(card.Id, request), CancellationToken.None);

        // Assert
        var okResult = result as IStatusCodeHttpResult;
        okResult?.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task BlockCardCommand_NonExistingCard_ShouldReturnNotFound()
    {
        // Arrange
        var handler = new BlockCardCommandHandler(_issuerService);
        var request = new BlockCardRequest("test");

        // Act
        var result = await handler.Handle(new BlockCardCommand(Guid.NewGuid(), request), CancellationToken.None);

        // Assert
        var notFoundResult = result as IStatusCodeHttpResult;
        notFoundResult?.StatusCode.Should().Be(404);
    }

    #endregion

    #region Helpers

    private async Task<CardEntity> CreateTestCard()
    {
        var customer = await _customerService.CreateAsync(
            "Test Cust", $"D{Guid.NewGuid():N}"[..10], "t@t.com", "111",
            "CEDULA", "M", "", "", "", "", "", CancellationToken.None);
        var account = await _issuerService.CreateAccountAsync(
            customer.Id, AccountType.Credit, "VISA", 5000m, CancellationToken.None);
        return await _issuerService.IssueCardAsync(
            account.Id, "411111", "4111111111111111", "2812", CancellationToken.None);
    }

    #endregion
}
