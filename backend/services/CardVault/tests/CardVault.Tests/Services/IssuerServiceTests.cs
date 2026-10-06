using System.Text;
using CardVault.Api.Vault;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

public sealed class IssuerServiceTests : IDisposable
{
    private readonly CardVault.Infrastructure.Persistence.CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly VaultCrypto _crypto;
    private readonly IssuerService _sut;
    private readonly CustomerService _customers;

    public IssuerServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _crypto = TestVaultCrypto.Create();
        _sut = new IssuerService(_db, _audit, _crypto);
        _customers = new CustomerService(_db);

        // Issuance only accepts BINs inside an enabled range; 8-digit ranges are seeded per test.
        SeedBinRange(400000, 499999);
        SeedBinRange(555555, 555555);
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
            account.Id, "411111", "2810", CancellationToken.None);

        // Assert
        card.Should().NotBeNull();
        card.Id.Should().NotBeEmpty();
        card.AccountId.Should().Be(account.Id);
        card.Bin.Should().Be("411111");
        card.PanToken.Should().StartWith("tok_");
        card.MaskedPan.Should().MatchRegex(@"^411111\*{6}[0-9]{4}$");
        card.Last4.Should().Be(card.MaskedPan[^4..]);
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
        var card = await _sut.IssueCardAsync(account.Id, "422222", "2712", CancellationToken.None);

        // Assert
        var vaultEntry = _db.TokenVault.FirstOrDefault(v => v.Token == card.PanToken);
        vaultEntry.Should().NotBeNull("each issued card must have a TokenVault entry");
        vaultEntry!.Bin.Should().Be("422222");
        vaultEntry.MaskedPan.Should().Be(card.MaskedPan);
    }

    [Fact]
    public async Task IssueCardAsync_ShouldCreateStatusHistory()
    {
        // Arrange
        var customer = await CreateTestCustomer();
        var account = await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA", 1000m, CancellationToken.None);

        // Act
        var card = await _sut.IssueCardAsync(account.Id, "555555", "2909", CancellationToken.None);

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
        await _sut.IssueCardAsync(account.Id, "555555", "2812", CancellationToken.None);

        // Assert
        var audits = await _audit.LatestAsync(10, CancellationToken.None);
        audits.Should().Contain(a => a.EventType == "issuer.card.issued");
    }

    #endregion

    #region IssueCardAsync — server-side PAN generation and vault storage (Gate 0 / T9)

    [Fact]
    public async Task IssueCardAsync_GeneratesA16DigitLuhnValidPanStartingWithTheBin()
    {
        var account = await CreateTestAccount();

        var card = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);
        var pan = DecryptPan(card);

        pan.Should().HaveLength(16);
        pan.Should().MatchRegex("^[0-9]{16}$");
        pan.Should().StartWith("411111");
        Luhn.IsValid(pan).Should().BeTrue($"'{pan}' must carry a correct Luhn check digit");
        card.MaskedPan.Should().Be($"{pan[..6]}******{pan[^4..]}");
        card.Last4.Should().Be(pan[^4..]);
    }

    [Fact]
    public async Task IssueCardAsync_AcceptsAn8DigitBinInsideAn8DigitRange()
    {
        SeedBinRange(53123400, 53123499);
        var account = await CreateTestAccount();

        var card = await _sut.IssueCardAsync(account.Id, "53123450", "2912", CancellationToken.None);
        var pan = DecryptPan(card);

        pan.Should().HaveLength(16);
        pan.Should().StartWith("53123450");
        Luhn.IsValid(pan).Should().BeTrue();
        card.MaskedPan.Should().Be($"531234******{pan[^4..]}");
    }

    [Fact]
    public async Task IssueCardAsync_StoresTheVaultRowThroughTheEncryptorNotAsBase64OfThePan()
    {
        var account = await CreateTestAccount();

        var card = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);
        var vault = _db.TokenVault.Single(v => v.Token == card.PanToken);
        var pan = DecryptPan(card);

        vault.KeyId.Should().Be("test-k1", "the key id must come from the cipher, not a placeholder");
        vault.CiphertextB64.Should().NotBe(Convert.ToBase64String(Encoding.UTF8.GetBytes(pan)));
        Encoding.UTF8.GetString(Convert.FromBase64String(vault.CiphertextB64)).Should().NotContain(pan);

        // Flip a tag byte: a real AEAD cipher must refuse to open the row.
        var tampered = Convert.FromBase64String(vault.TagB64);
        tampered[0] ^= 0xFF;
        var act = () => _crypto.DecryptFromParts<IssuedCardPayload>(vault.KeyId, vault.NonceB64, vault.CiphertextB64, Convert.ToBase64String(tampered));
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public async Task IssueCardAsync_TwoCardsOnTheSameBinGetDifferentPansAndNonces()
    {
        var account = await CreateTestAccount();

        var first = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);
        var second = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);

        DecryptPan(first).Should().NotBe(DecryptPan(second));
        var rows = _db.TokenVault.Where(v => v.Token == first.PanToken || v.Token == second.PanToken).ToList();
        rows.Select(r => r.NonceB64).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task IssueCardAsync_NeverReusesAMaskedPanThatAlreadyExistsInTheVault()
    {
        // No blind index exists on TokenVault yet, so uniqueness is enforced on MaskedPan (first 6 + last 4).
        var account = await CreateTestAccount();
        var existing = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);
        var preExisting = _db.TokenVault.Select(v => v.MaskedPan).ToHashSet();

        var card = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);

        preExisting.Should().NotContain(card.MaskedPan);
        card.MaskedPan.Should().NotBe(existing.MaskedPan);
    }

    [Theory]
    [InlineData("611111", "it is outside every enabled range")]
    [InlineData("399999", "it is just below the enabled range")]
    public async Task IssueCardAsync_RejectsABinOutsideEveryEnabledRange(string bin, string because)
    {
        var account = await CreateTestAccount();

        var act = () => _sut.IssueCardAsync(account.Id, bin, "2912", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>(because)).WithMessage("*BIN*enabled*");
        _db.Cards.Should().BeEmpty();
        _db.TokenVault.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueCardAsync_RejectsABinWhoseRangeIsDisabled()
    {
        _db.BinRanges.Add(new BinRangeEntity { BinStart = 600000, BinEnd = 699999, Brand = "MASTERCARD", Product = "CREDIT", Enabled = false });
        _db.SaveChanges();
        var account = await CreateTestAccount();

        var act = () => _sut.IssueCardAsync(account.Id, "611111", "2912", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.Cards.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("4111")]
    [InlineData("4111111")]
    [InlineData("41111a")]
    [InlineData("4111111111111111")]
    public async Task IssueCardAsync_RejectsAMalformedBin(string bin)
    {
        var account = await CreateTestAccount();

        var act = () => _sut.IssueCardAsync(account.Id, bin, "2912", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*6 or 8 digits*");
        _db.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueCardAsync_AuditPayloadNeverContainsThePan()
    {
        var account = await CreateTestAccount();

        var card = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);
        var pan = DecryptPan(card);

        var audits = await _audit.LatestAsync(10, CancellationToken.None);
        var issued = audits.Single(a => a.EventType == "issuer.card.issued");
        issued.PayloadJson.Should().NotContain(pan);
        issued.PayloadJson.Should().Contain(card.MaskedPan);
    }

    [Fact]
    public async Task ReplaceCardAsync_IssuesTheReplacementWithAFreshServerGeneratedPan()
    {
        var old = await CreateTestCard();

        var (error, newCard) = await _sut.ReplaceCardAsync(old.Id, "damaged", CancellationToken.None);

        error.Should().Be(CardLifecycleError.None);
        var pan = DecryptPan(newCard!);
        pan.Should().StartWith(old.Bin);
        Luhn.IsValid(pan).Should().BeTrue();
        pan.Should().NotBe(DecryptPan(old));
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

    private async Task<CardAccountEntity> CreateTestAccount()
    {
        var customer = await CreateTestCustomer();
        return await _sut.CreateAccountAsync(customer.Id, AccountType.Credit, "VISA_TEST", 5000m, CancellationToken.None);
    }

    private async Task<CardEntity> CreateTestCard()
    {
        var account = await CreateTestAccount();
        return await _sut.IssueCardAsync(account.Id, "411111", "2712", CancellationToken.None);
    }

    private void SeedBinRange(int binStart, int binEnd)
    {
        _db.BinRanges.Add(new BinRangeEntity { BinStart = binStart, BinEnd = binEnd, Brand = "TEST", Product = "CREDIT", Enabled = true });
        _db.SaveChanges();
    }

    /// <summary>Opens the vault row with the same cipher the service used; this is the only way a test may see a PAN.</summary>
    private string DecryptPan(CardEntity card)
    {
        var vault = _db.TokenVault.Single(v => v.Token == card.PanToken);
        return _crypto.DecryptFromParts<IssuedCardPayload>(vault.KeyId, vault.NonceB64, vault.CiphertextB64, vault.TagB64).Pan;
    }

    #endregion
}
