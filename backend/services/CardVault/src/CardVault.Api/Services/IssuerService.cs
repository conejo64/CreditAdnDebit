using System.Security.Cryptography;
using System.Text;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Vault;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public enum CardLifecycleError { None = 0, NotFound, InvalidStatus }

public sealed class IssuerService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public IssuerService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
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

    public async Task<CardEntity> IssueCardAsync(Guid accountId, string bin, string pan, string expiryYyMm, CancellationToken ct)
    {
        // Tokenize PAN (PCI: store only token + masked PAN)
        var token = "tok_" + Guid.NewGuid().ToString("N")[..16];

        var masked = MaskPan(pan);
        var last4 = pan.Length >= 4 ? pan[^4..] : pan;

        // minimal "encryption" is already used by /api/tokens/tokenize; we will store a vault entry with placeholder crypto fields
        var vault = new TokenVaultEntryEntity
        {
            Id = Guid.NewGuid(),
            Token = token,
            KeyId = "dev-key",
            NonceB64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)),
            CiphertextB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pan)), // dev-only placeholder
            TagB64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
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

        // Issue new card on the same account (same BIN, same expiry pattern)
        var newCard = await IssueCardAsync(old.AccountId, old.Bin, Guid.NewGuid().ToString("N")[..16], old.ExpiryYyMm, ct);

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

    private static string MaskPan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length < 10) return "****";
        var first6 = pan[..6];
        var last4 = pan[^4..];
        return $"{first6}******{last4}";
    }
}
