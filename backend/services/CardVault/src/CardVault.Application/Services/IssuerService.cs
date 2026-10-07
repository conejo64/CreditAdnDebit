using CardVault.Application.Ports;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Vault;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public enum CardLifecycleError { None = 0, NotFound, InvalidStatus }

/// <summary>
/// Plaintext that the vault cipher seals for an issued card. Property names are the wire contract with the
/// tokenization endpoints (<c>CardVault.Api.Vault.TokenVaultService.SensitiveCardPayload</c> serializes to the
/// same JSON), so a card issued here can be detokenized through <c>/api/tokens</c> like any other vault entry.
/// </summary>
public sealed record IssuedCardPayload(string Pan, string? ExpiryYyMm);

public sealed class IssuerService
{
    /// <summary>Draws before issuance gives up on an improbable run of masked-PAN collisions.</summary>
    private const int MaxPanDraws = 10;

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly IContactDataEncryptor _encryptor;

    public IssuerService(CardVaultDbContext db, AuditService audit, IContactDataEncryptor encryptor)
    {
        _db = db;
        _audit = audit;
        _encryptor = encryptor;
    }

    public async Task<CardAccountEntity> CreateAccountAsync(Guid customerId, AccountType type, string productCode, decimal creditLimit, CancellationToken ct)
    {
        var acc = new CardAccountEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            AccountNumber = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            AccountType = type,
            ProductCode = productCode,
            CreditLimit = type == AccountType.Credit ? creditLimit : 0m,
            AvailableLimit = type == AccountType.Credit ? creditLimit : 0m,
            CreatedOn = DateTimeOffset.UtcNow
        };
        _db.Accounts.Add(acc);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("issuer.account.created", new { accountId = acc.Id, customerId, type = type.ToString(), productCode, creditLimit }, correlationId: null, traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(), ct: ct);

        return acc;
    }

    /// <summary>
    /// Issues a card on <paramref name="accountId"/>. The PAN is generated here from <paramref name="bin"/>
    /// (which must be 6 or 8 digits inside an enabled catalog range), sealed with the vault cipher, and never
    /// returned: callers get the token, the mask and the last four digits.
    /// </summary>
    /// <exception cref="InvalidOperationException">The BIN is malformed, unknown or disabled, or no unique PAN could be drawn.</exception>
    public async Task<CardEntity> IssueCardAsync(Guid accountId, string bin, string expiryYyMm, CancellationToken ct)
    {
        bin = bin?.Trim() ?? string.Empty;
        await EnsureBinIsIssuableAsync(bin, ct);

        var pan = await DrawUniquePanAsync(bin, ct);
        var masked = MaskPan(pan);
        var last4 = pan[^4..];

        // PCI: the clear PAN lives only in this method; the vault row holds AES-GCM parts from the active key.
        var token = "tok_" + Guid.NewGuid().ToString("N")[..16];
        var (keyId, nonceB64, cipherB64, tagB64) = _encryptor.EncryptToParts(new IssuedCardPayload(pan, expiryYyMm));
        var vault = new TokenVaultEntryEntity
        {
            Id = Guid.NewGuid(),
            Token = token,
            KeyId = keyId,
            NonceB64 = nonceB64,
            CiphertextB64 = cipherB64,
            TagB64 = tagB64,
            MaskedPan = masked,
            Bin = bin,
            CreatedOn = DateTimeOffset.UtcNow
        };
        _db.TokenVault.Add(vault);

        var card = new CardEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Bin = bin,
            PanToken = token,
            MaskedPan = masked,
            ExpiryYyMm = expiryYyMm,
            Last4 = last4,
            Status = CardStatus.Created,
            CreatedOn = DateTimeOffset.UtcNow
        };
        _db.Cards.Add(card);
        _db.CardStatusHistory.Add(new CardStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            FromStatus = CardStatus.Created,
            ToStatus = CardStatus.Created,
            Reason = "issued",
            ChangedOn = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("issuer.card.issued", new { cardId = card.Id, accountId, bin, maskedPan = masked, expiryYyMm }, correlationId: null, traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(), ct: ct);

        return card;
    }

    public async Task<CardEntity?> ChangeStatusAsync(Guid cardId, CardStatus to, string reason, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (card is null) return null;

        var from = card.Status;
        card.Status = to;

        _db.CardStatusHistory.Add(new CardStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            FromStatus = from,
            ToStatus = to,
            Reason = reason,
            ChangedOn = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("issuer.card.status_changed", new { cardId, from = from.ToString(), to = to.ToString(), reason }, correlationId: null, traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(), ct: ct);

        return card;
    }

    public Task<CardEntity?> GetCardAsync(Guid id, CancellationToken ct) =>
        _db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<(CardLifecycleError Error, CardEntity? Card)> BlockCardAsync(Guid cardId, string reason, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (card is null) return (CardLifecycleError.NotFound, null);

        var result = await ChangeStatusAsync(cardId, CardStatus.Blocked, reason, ct);

        await _audit.WriteAsync("issuer.card.blocked",
            new { cardId, reason },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return (CardLifecycleError.None, result);
    }

    public async Task<(CardLifecycleError Error, CardEntity? Card)> UnblockCardAsync(Guid cardId, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (card is null) return (CardLifecycleError.NotFound, null);
        if (card.Status != CardStatus.Blocked) return (CardLifecycleError.InvalidStatus, null);

        var result = await ChangeStatusAsync(cardId, CardStatus.Active, "unblocked", ct);

        await _audit.WriteAsync("issuer.card.unblocked",
            new { cardId },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return (CardLifecycleError.None, result);
    }

    public async Task<(CardLifecycleError Error, CardEntity? Card)> CancelCardAsync(Guid cardId, string? reason, CancellationToken ct)
    {
        var card = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (card is null) return (CardLifecycleError.NotFound, null);
        if (card.Status == CardStatus.Cancelled) return (CardLifecycleError.InvalidStatus, null);

        var result = await ChangeStatusAsync(cardId, CardStatus.Cancelled, reason ?? "cancelled", ct);

        await _audit.WriteAsync("issuer.card.cancelled",
            new { cardId, reason },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return (CardLifecycleError.None, result);
    }

    public async Task<(CardLifecycleError Error, CardEntity? NewCard)> ReplaceCardAsync(Guid cardId, string? reason, CancellationToken ct)
    {
        var old = await _db.Cards.FirstOrDefaultAsync(x => x.Id == cardId, ct);
        if (old is null) return (CardLifecycleError.NotFound, null);
        if (old.Status == CardStatus.Cancelled) return (CardLifecycleError.InvalidStatus, null);

        // Cancel the old card
        var fromStatus = old.Status;
        old.Status = CardStatus.Cancelled;
        _db.CardStatusHistory.Add(new CardStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            CardId = old.Id,
            FromStatus = fromStatus,
            ToStatus = CardStatus.Cancelled,
            Reason = reason ?? "replaced",
            ChangedOn = DateTimeOffset.UtcNow
        });

        // Issue new card on the same account (same BIN, same expiry pattern) with a freshly generated PAN
        var newCard = await IssueCardAsync(old.AccountId, old.Bin, old.ExpiryYyMm, ct);

        // Bidirectional audit linkage (spec ILB-CL-2-S1)
        _db.CardStatusHistory.Add(new CardStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            CardId = old.Id,
            FromStatus = CardStatus.Cancelled,
            ToStatus = CardStatus.Cancelled,
            Reason = $"replaced->{newCard.Id}",
            ChangedOn = DateTimeOffset.UtcNow
        });
        _db.CardStatusHistory.Add(new CardStatusHistoryEntity
        {
            Id = Guid.NewGuid(),
            CardId = newCard.Id,
            FromStatus = CardStatus.Created,
            ToStatus = CardStatus.Created,
            Reason = $"replacement-of:{old.Id}",
            ChangedOn = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("issuer.card.replaced",
            new { oldCardId = cardId, newCardId = newCard.Id, reason },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        return (CardLifecycleError.None, newCard);
    }

    private async Task EnsureBinIsIssuableAsync(string bin, CancellationToken ct)
    {
        if (!PanGenerator.IsWellFormedBin(bin))
            throw new InvalidOperationException($"BIN '{bin}' is not valid: a BIN must be exactly 6 or 8 digits.");

        var ranges = await _db.BinRanges.AsNoTracking()
            .Where(r => r.Enabled)
            .Select(r => new { r.BinStart, r.BinEnd })
            .ToListAsync(ct);

        if (!ranges.Any(r => PanGenerator.BinMatchesRange(bin, r.BinStart, r.BinEnd)))
            throw new InvalidOperationException($"BIN '{bin}' does not belong to any enabled BIN range of this issuer.");
    }

    /// <summary>
    /// Draws PANs until one is not already present. <c>TokenVault</c> has no blind index of the PAN (adding one
    /// is a schema change outside this slice), so uniqueness is checked on the masked form (BIN + last four),
    /// which is strictly coarser than PAN equality: any true collision is caught, at the cost of a rare retry.
    /// </summary>
    private async Task<string> DrawUniquePanAsync(string bin, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxPanDraws; attempt++)
        {
            var pan = PanGenerator.Generate(bin);
            var masked = MaskPan(pan);

            var taken = await _db.TokenVault.AnyAsync(v => v.MaskedPan == masked, ct)
                        || await _db.Cards.AnyAsync(c => c.MaskedPan == masked, ct);
            if (!taken) return pan;
        }

        throw new InvalidOperationException($"Could not draw a unique PAN for BIN '{bin}' after {MaxPanDraws} attempts.");
    }

    private static string MaskPan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length < 10) return "****";
        var first6 = pan[..6];
        var last4 = pan[^4..];
        return $"{first6}******{last4}";
    }
}
