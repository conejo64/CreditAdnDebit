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
/// SEC-02 verify-then-upgrade transition: a successful verify of a legacy unsalted-SHA-256
/// `PinHash` transparently re-hashes the same PIN with Argon2id in the same
/// `SaveChangesAsync`, and the old unsalted hash is destroyed — satisfying "After transition,
/// no card is verifiable only by unsalted SHA-256".
/// </summary>
public sealed class PinTransitionTests : IDisposable
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly PinService _sut;

    public PinTransitionTests()
    {
        _db = TestDbContextFactory.Create();
        _audit = new AuditService(_db);
        _sut = new PinService(_db, _audit);
    }

    public void Dispose() => _db.Dispose();

    private static string LegacySha256(string pin)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(pin));
        return Convert.ToBase64String(bytes);
    }

    private async Task<CardEntity> CreateLegacyCardAsync(string pin)
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

        // Legacy record: unsalted SHA-256 PinHash, PinHashAlgorithm/PinSalt/PinHashParams all null —
        // exactly the pre-SEC-02 shape.
        var card = new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Bin = "411111",
            PanToken = Guid.NewGuid().ToString("N")[..32],
            MaskedPan = "411111******1111",
            ExpiryYyMm = "2812",
            Last4 = "1111",
            Status = CardStatus.Active,
            PinHash = LegacySha256(pin),
            PinHashAlgorithm = null,
            PinSalt = null,
            PinHashParams = null
        };
        _db.Cards.Add(card);

        await _db.SaveChangesAsync(CancellationToken.None);
        return card;
    }

    [Fact]
    public async Task VerifyPinAsync_LegacyNullAlgorithm_VerifiesAgainstUnsaltedSha256()
    {
        var card = await CreateLegacyCardAsync("4321");

        var result = await _sut.VerifyPinAsync(card.Id, "4321", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyPinAsync_SuccessfulLegacyVerify_UpgradesToArgon2idAndDestroysOldHash()
    {
        var card = await CreateLegacyCardAsync("1122");
        var oldUnsaltedHash = card.PinHash;

        var result = await _sut.VerifyPinAsync(card.Id, "1122", CancellationToken.None);

        result.Should().BeTrue();

        var upgraded = await _db.Cards.FindAsync(card.Id);
        upgraded!.PinHashAlgorithm.Should().Be("argon2id");
        upgraded.PinSalt.Should().NotBeNullOrEmpty();
        upgraded.PinHashParams.Should().NotBeNullOrEmpty();
        upgraded.PinHash.Should().NotBe(oldUnsaltedHash, "the old unsalted hash must be gone, not retained anywhere");
    }

    [Fact]
    public async Task VerifyPinAsync_AfterUpgrade_OldUnsaltedComparisonNoLongerMatchesCurrentHash()
    {
        var card = await CreateLegacyCardAsync("6655");

        await _sut.VerifyPinAsync(card.Id, "6655", CancellationToken.None);

        var upgraded = await _db.Cards.FindAsync(card.Id);
        var oldStyleComparisonHash = LegacySha256("6655");

        upgraded!.PinHash.Should().NotBe(
            oldStyleComparisonHash,
            "attempting to verify using the old unsalted-SHA-256 comparison against the now-current PinHash must fail");
    }
}
