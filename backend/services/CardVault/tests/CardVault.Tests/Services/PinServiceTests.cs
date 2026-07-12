using System.Security.Cryptography;
using System.Text;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Tests.Infrastructure;
using FluentAssertions;

namespace CardVault.Tests.Services;

/// <summary>
/// SEC-02: Salted, cost-parameterized PIN hashing (Argon2id interim).
/// </summary>
public sealed class PinServiceTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PinService _sut;

    public PinServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _sut = new PinService(_db, _audit);
    }

    public void Dispose() => _db.Dispose();

    private async Task<CardEntity> CreateTestCardAsync()
    {
        var customer = new CustomerEntity
        {
            Id = Guid.NewGuid(),
            CustomerNumber = $"CUS{Guid.NewGuid():N}"[..20],
            FullName = "Test Customer",
            DocumentId = Guid.NewGuid().ToString("N")[..15],
            Email = $"{Guid.NewGuid():N}@test.com",
            Phone = "0999999999"
        };
        _db.Customers.Add(customer);

        var account = new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AccountNumber = $"ACC{Guid.NewGuid():N}"[..20],
            ProductCode = "VISA_CLASSIC",
            AccountType = AccountType.Credit,
            CreditLimit = 1000m,
            AvailableLimit = 1000m
        };
        _db.Accounts.Add(account);

        var card = new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Bin = "411111",
            PanToken = Guid.NewGuid().ToString("N")[..32],
            MaskedPan = "411111******1111",
            ExpiryYyMm = "2812",
            Last4 = "1111",
            Status = CardStatus.Active
        };
        _db.Cards.Add(card);

        await _db.SaveChangesAsync(CancellationToken.None);
        return card;
    }

    private static string LegacySha256(string pin)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(pin));
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task SetPinAsync_StoresArgon2idAlgorithmSaltAndParams_NotUnsaltedSha256()
    {
        var card = await CreateTestCardAsync();

        await _sut.SetPinAsync(card.Id, "1234", CancellationToken.None);

        var stored = await _db.Cards.FindAsync(card.Id);
        stored!.PinHashAlgorithm.Should().Be("argon2id");
        stored.PinSalt.Should().NotBeNullOrEmpty();
        stored.PinHashParams.Should().NotBeNullOrEmpty();
        stored.PinHash.Should().NotBeNullOrEmpty();
        stored.PinHash.Should().NotBe(LegacySha256("1234"), "the stored hash must not be an unsalted SHA-256 of the PIN");
    }

    [Fact]
    public async Task SetPinAsync_TwoCardsSamePin_ProduceDifferentHashesAndDifferentSalts()
    {
        var cardA = await CreateTestCardAsync();
        var cardB = await CreateTestCardAsync();

        await _sut.SetPinAsync(cardA.Id, "5678", CancellationToken.None);
        await _sut.SetPinAsync(cardB.Id, "5678", CancellationToken.None);

        var storedA = await _db.Cards.FindAsync(cardA.Id);
        var storedB = await _db.Cards.FindAsync(cardB.Id);

        storedA!.PinSalt.Should().NotBe(storedB!.PinSalt, "each PIN must use a distinct random salt");
        storedA.PinHash.Should().NotBe(storedB.PinHash, "identical PINs on different cards must not produce identical hashes");
    }

    [Fact]
    public async Task VerifyPinAsync_Argon2idPath_CorrectPinSucceeds_IncorrectPinFails()
    {
        var card = await CreateTestCardAsync();
        await _sut.SetPinAsync(card.Id, "9876", CancellationToken.None);

        var correct = await _sut.VerifyPinAsync(card.Id, "9876", CancellationToken.None);
        var incorrect = await _sut.VerifyPinAsync(card.Id, "0000", CancellationToken.None);

        correct.Should().BeTrue();
        incorrect.Should().BeFalse();
    }
}
