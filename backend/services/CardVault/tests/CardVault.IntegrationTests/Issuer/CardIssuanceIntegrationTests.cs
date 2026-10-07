using System.Text;
using CardVault.Application.Services;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence.Catalog;
using CardVault.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CardVault.IntegrationTests.Issuer;

/// <summary>
/// Gate 0 / T9 on real PostgreSQL: issuing a card generates the PAN server-side from an enabled BIN
/// range, stores it in <c>TokenVault</c> only as AES-GCM ciphertext, and keeps every persisted
/// column within the limits the relational schema enforces (the InMemory provider ignores them).
/// </summary>
public sealed class CardIssuanceIntegrationTests : IntegrationTestBase
{
    private readonly AesGcmTestEncryptor _encryptor = new();
    private IssuerService _sut = null!;

    public CardIssuanceIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
    }

    protected override void OnDbReady()
    {
        _sut = new IssuerService(Db, new AuditService(Db), _encryptor);
    }

    [Fact(DisplayName = "Issuing a card stores only AES-GCM ciphertext in TokenVault and the PAN passes Luhn")]
    public async Task Issued_card_is_vaulted_as_ciphertext_and_is_Luhn_valid()
    {
        var account = await SeedCreditAccountAsync();
        Db.BinRanges.Add(new BinRangeEntity { BinStart = 400000, BinEnd = 499999, Brand = "VISA", Product = "CREDIT", Enabled = true });
        await Db.SaveChangesAsync();

        var card = await _sut.IssueCardAsync(account.Id, "411111", "2912", CancellationToken.None);

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        var vault = await reader.TokenVault.AsNoTracking().SingleAsync(v => v.Token == card.PanToken);
        var stored = await reader.Cards.AsNoTracking().SingleAsync(c => c.Id == card.Id);
        var audit = await reader.AuditEvents.AsNoTracking().SingleAsync(a => a.EventType == "issuer.card.issued");

        var pan = _encryptor.DecryptFromParts<IssuedCardPayload>(vault.KeyId, vault.NonceB64, vault.CiphertextB64, vault.TagB64).Pan;

        pan.Should().HaveLength(16);
        pan.Should().StartWith("411111");
        Luhn.IsValid(pan).Should().BeTrue();

        vault.KeyId.Should().Be(AesGcmTestEncryptor.KeyId);
        vault.CiphertextB64.Should().NotBe(Convert.ToBase64String(Encoding.UTF8.GetBytes(pan)));
        vault.MaskedPan.Should().Be($"411111******{pan[^4..]}");
        vault.Bin.Should().Be("411111");

        stored.MaskedPan.Should().Be(vault.MaskedPan);
        stored.Last4.Should().Be(pan[^4..]);
        stored.PanToken.Should().Be(vault.Token);

        audit.PayloadJson.Should().NotContain(pan);
    }

    [Fact(DisplayName = "A BIN outside every enabled range is rejected before anything is written")]
    public async Task Bin_outside_enabled_ranges_is_rejected()
    {
        var account = await SeedCreditAccountAsync();
        Db.BinRanges.Add(new BinRangeEntity { BinStart = 400000, BinEnd = 499999, Brand = "VISA", Product = "CREDIT", Enabled = true });
        await Db.SaveChangesAsync();

        var act = () => _sut.IssueCardAsync(account.Id, "511111", "2912", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var reader = PostgresFixture.CreateSiblingContext(Db);
        (await reader.Cards.CountAsync()).Should().Be(0);
        (await reader.TokenVault.CountAsync()).Should().Be(0);
    }
}
