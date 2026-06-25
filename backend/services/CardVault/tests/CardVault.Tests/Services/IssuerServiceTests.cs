using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

public sealed class IssuerServiceTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly IssuerService _sut;
    private readonly CustomerService _customers;

    public IssuerServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _sut = new IssuerService(_db, _audit);
        _customers = new CustomerService(_db);
    }

    public void Dispose() => _db.Dispose();

    #region CreateAccountAsync

    [Fact]
    public async Task CreateAccountAsync_CreditAccount_ShouldSetLimits()
    {
        // Arrange
        var customer = await CreateTestCustomer();

        // Act
        var account = await _sut.CreateAccountAsync(
            customer.Id, AccountType.Credit, "VISA_CLASSIC", 5000m, CancellationToken.None);

        // Assert
        account.Should().NotBeNull();
        account.Id.Should().NotBeEmpty();
        account.CustomerId.Should().Be(customer.Id);
        account.AccountType.Should().Be(AccountType.Credit);
        account.ProductCode.Should().Be("VISA_CLASSIC");
        account.CreditLimit.Should().Be(5000m);
        account.AvailableLimit.Should().Be(5000m);
    }

    [Fact]
    public async Task CreateAccountAsync_DebitAccount_ShouldSetZeroLimit()
    {
        // Arrange
        var customer = await CreateTestCustomer();

        // Act
        var account = await _sut.CreateAccountAsync(
            customer.Id, AccountType.Debit, "VISA_DEBIT", 10000m, CancellationToken.None);

        // Assert
        account.CreditLimit.Should().Be(0m, "debit accounts should have zero credit limit regardless of input");
        account.AvailableLimit.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAccountAsync_ShouldGenerateAuditRecord()
    {
        // Arrange
        var customer = await CreateTestCustomer();

        // Act
        await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "MC_PLAT", 3000m, CancellationToken.None);

        // Assert
        var audits = await _audit.LatestAsync(10, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.account.created");
    }

    #endregion

    #region IssueCardAsync

    [Fact]
    public async Task IssueCardAsync_ShouldCreateCardWithToken()
    {
        // Arrange
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA_CLAS", 2000m, CancellationToken.None);

        // Act
        var card = await _sut.IssueCardAsync(
            account.Id, "411111", "4111111111111111", "2810", CancellationToken.None);

        // Assert
        card.Should().NotBeNull();
        card.Id.Should().NotBeEmpty();
        card.AccountId.Should().Be(account.Id);
        card.Bin.Should().Be("411111");
        card.PanToken.Should().StartWith("tok_");
        card.MaskedPan.Should().Be("411111******1111");
        card.Last4.Should().Be("1111");
        card.ExpiryYyMm.Should().Be("2810");
        card.Status.Should().Be(CardStatus.Created);
    }

    [Fact]
    public async Task IssueCardAsync_ShouldCreateTokenVaultEntry()
    {
        // Arrange
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA", 1000m, CancellationToken.None);

        // Act
        var card = await _sut.IssueCardAsync(account.Id, "422222", "4222222222222222", "2712", CancellationToken.None);

        // Assert
        var vaultEntry = _db.TokenVault.FirstOrDefault(v => v.Token == card.PanToken);
        vaultEntry.Should().NotBeNull("each issued card must have a TokenVault entry");
        vaultEntry!.Bin.Should().Be("422222");
        vaultEntry.MaskedPan.Should().Be("422222******2222");
    }

    [Fact]
    public async Task IssueCardAsync_ShouldCreateStatusHistory()
    {
        // Arrange
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA", 1000m, CancellationToken.None);

        // Act
        var card = await _sut.IssueCardAsync(account.Id, "555555", "5555555555554444", "2909", CancellationToken.None);

        // Assert
        var history = _db.CardStatusHistory.Where(h => h.CardId == card.Id).ToList();
        history.Should().HaveCount(1);
        history[0].FromStatus.Should().Be(CardStatus.Created);
        history[0].ToStatus.Should().Be(CardStatus.Created);
        history[0].Reason.Should().Be("issued");
    }

    [Fact]
    public async Task IssueCardAsync_ShouldWriteAuditEvent()
    {
        // Arrange
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "MC", 500m, CancellationToken.None);

        // Act
        await _sut.IssueCardAsync(account.Id, "555555", "5555555555554444", "2812", CancellationToken.None);

        // Assert
        var audits = await _audit.LatestAsync(10, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.card.issued");
    }

    #endregion

    #region ChangeStatusAsync

    [Fact]
    public async Task ChangeStatusAsync_ExistingCard_ShouldUpdateAndLog()
    {
        // Arrange
        var card = await CreateTestCard();

        // Act
        var updated = await _sut.ChangeStatusAsync(card.Id, CardStatus.Active, "activated by admin", CancellationToken.None);

        // Assert
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(CardStatus.Active);

        var history = _db.CardStatusHistory
            .Where(h => h.CardId == card.Id)
            .OrderByDescending(h => h.ChangedOn)
            .ToList();

        history.Should().HaveCountGreaterThanOrEqualTo(2, "should have initial + activation entries");
        history[0].FromStatus.Should().Be(CardStatus.Created);
        history[0].ToStatus.Should().Be(CardStatus.Active);
        history[0].Reason.Should().Be("activated by admin");
    }

    [Fact]
    public async Task ChangeStatusAsync_NonExistingCard_ShouldReturnNull()
    {
        // Act
        var result = await _sut.ChangeStatusAsync(Guid.NewGuid(), CardStatus.Blocked, "test", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ChangeStatusAsync_BlockThenActivate_ShouldTrackFullHistory()
    {
        // Arrange
        var card = await CreateTestCard();

        // Act
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Active, "activate", CancellationToken.None);
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Blocked, "suspicious activity", CancellationToken.None);
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Active, "cleared", CancellationToken.None);

        // Assert
        var history = _db.CardStatusHistory.Where(h => h.CardId == card.Id).ToList();
        history.Should().HaveCount(4, "issued + 3 status changes");
    }

    #endregion

    #region GetCardAsync

    [Fact]
    public async Task GetCardAsync_ExistingCard_ShouldReturnCard()
    {
        // Arrange
        var card = await CreateTestCard();

        // Act
        var found = await _sut.GetCardAsync(card.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(card.Id);
        found.PanToken.Should().StartWith("tok_");
    }

    [Fact]
    public async Task GetCardAsync_NonExistingId_ShouldReturnNull()
    {
        var result = await _sut.GetCardAsync(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeNull();
    }

    #endregion

    #region Named Audit Events — Gap 4

    /// <summary>
    /// GAP-4 (RED): Spec requires UnblockCardAsync to emit a named 'issuer.card.unblocked'
    /// audit event in addition to the generic 'issuer.card.status_changed'.
    /// Fails until UnblockCardAsync calls _audit.WriteAsync("issuer.card.unblocked").
    /// </summary>
    [Fact]
    public async Task UnblockCardAsync_ShouldEmitNamedUnblockedAuditEvent()
    {
        // Arrange — create card, activate, then block it
        var card = await CreateTestCard();
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Active, "activated", CancellationToken.None);
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Blocked, "fraud suspicion", CancellationToken.None);

        // Act
        await _sut.UnblockCardAsync(card.Id, CancellationToken.None);

        // Assert — named event must exist in addition to generic status_changed
        var audits = await _audit.LatestAsync(50, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.card.unblocked",
            because: "spec requires a named 'issuer.card.unblocked' event, not just the generic status_changed");
    }

    /// <summary>
    /// GAP-4 (RED): Spec requires CancelCardAsync to emit a named 'issuer.card.cancelled'
    /// audit event in addition to the generic 'issuer.card.status_changed'.
    /// Fails until CancelCardAsync calls _audit.WriteAsync("issuer.card.cancelled").
    /// </summary>
    [Fact]
    public async Task CancelCardAsync_ShouldEmitNamedCancelledAuditEvent()
    {
        // Arrange
        var card = await CreateTestCard();
        await _sut.ChangeStatusAsync(card.Id, CardStatus.Active, "activated", CancellationToken.None);

        // Act
        await _sut.CancelCardAsync(card.Id, "client request", CancellationToken.None);

        // Assert
        var audits = await _audit.LatestAsync(50, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.card.cancelled",
            because: "spec requires a named 'issuer.card.cancelled' event, not just the generic status_changed");
    }

    #endregion

    #region Helpers

    private async Task<CustomerEntity> CreateTestCustomer()
    {
        return await _customers.CreateAsync(
            "Test Customer", $"DOC{Guid.NewGuid():N}"[..12], "test@bank.com", "+593999999999",
            "CEDULA", "M", "Test Address", "Stmt Addr", "City", "City", "City", CancellationToken.None);
    }

    private async Task<CardEntity> CreateTestCard()
    {
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA_TEST", 5000m, CancellationToken.None);
        return await _sut.IssueCardAsync(account.Id, "411111", "4111111111111111", "2712", CancellationToken.None);
    }

    #endregion
}
