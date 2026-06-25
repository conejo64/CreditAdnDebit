using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardVault.Application.Contracts;
using Microsoft.Extensions.Hosting;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Wallets;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class WalletService
{
    private const string WalletTokenTopic = "cardvault.wallet.token.changed";
    private const string WalletAuthorizationTopic = "cardvault.wallet.authorization.completed";

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly HoldService _holds;
    private readonly IHostEnvironment _environment;

    public WalletService(CardVaultDbContext db, AuditService audit, HoldService holds, IHostEnvironment environment)
    {
        _db = db;
        _audit = audit;
        _holds = holds;
        _environment = environment;
    }

    public async Task<WalletEnrollmentView> RegisterAsync(RegisterWalletTokenRequest request, CancellationToken ct)
    {
        var card = await _db.Cards
            .Include(x => x.Account)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CardId, ct)
            ?? throw new InvalidOperationException("Card not found.");

        if (card.Status != CardStatus.Active)
            throw new InvalidOperationException("Only active cards can be enrolled into wallets.");

        var provider = request.Provider.Trim().ToUpperInvariant();
        var tokenReference = $"WLT_{Guid.NewGuid():N}"[..28];
        var activationCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var entity = new WalletTokenEntity
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AccountId = card.AccountId,
            CustomerId = card.Account.CustomerId,
            Provider = provider,
            DeviceReference = request.DeviceReference.Trim(),
            WalletReference = string.IsNullOrWhiteSpace(request.WalletReference) ? null : request.WalletReference.Trim(),
            TokenReference = tokenReference,
            AuthenticationMethod = request.AuthenticationMethod.Trim().ToUpperInvariant(),
            Status = WalletTokenStatus.PendingActivation,
            ActivationCodeHash = Hash(activationCode),
            ActivationHint = $"***{activationCode[^2..]}",
            ActivationExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedOn = DateTimeOffset.UtcNow
        };

        _db.WalletTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        await PublishTokenStateAsync("wallet.token.registered", entity, ct);
        return new WalletEnrollmentView(Map(entity), _environment.IsDevelopment() ? activationCode : null);
    }

    public async Task<WalletTokenView> ActivateAsync(Guid walletTokenId, ActivateWalletTokenRequest request, CancellationToken ct)
    {
        var entity = await _db.WalletTokens.FirstOrDefaultAsync(x => x.Id == walletTokenId, ct)
            ?? throw new InvalidOperationException("Wallet token not found.");

        if (entity.Status != WalletTokenStatus.PendingActivation)
            throw new InvalidOperationException("Wallet token is not pending activation.");

        if (entity.ActivationExpiresOn.HasValue && entity.ActivationExpiresOn.Value < DateTimeOffset.UtcNow)
        {
            entity.Status = WalletTokenStatus.Expired;
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Activation challenge expired.");
        }

        if (!string.Equals(entity.ActivationCodeHash, Hash(request.ActivationCode.Trim()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid activation code.");

        entity.Status = WalletTokenStatus.Active;
        entity.ActivatedOn = DateTimeOffset.UtcNow;
        entity.ActivationCodeHash = null;
        entity.ActivationExpiresOn = null;

        await _db.SaveChangesAsync(ct);
        await PublishTokenStateAsync("wallet.token.activated", entity, ct);
        return Map(entity);
    }

    public async Task<IReadOnlyList<WalletTokenView>> GetByCardAsync(Guid cardId, CancellationToken ct)
    {
        var items = await _db.WalletTokens.AsNoTracking()
            .Where(x => x.CardId == cardId)
            .OrderByDescending(x => x.CreatedOn)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task<WalletAuthorizationView> AuthorizeAsync(AuthorizeWalletPaymentRequest request, CancellationToken ct)
    {
        var existing = await _db.WalletAuthorizations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientTransactionId == request.ClientTransactionId, ct);
        if (existing is not null)
            return Map(existing);

        var postedOn = request.PostedOn ?? DateTimeOffset.UtcNow;
        var tokenReference = request.TokenReference.Trim().ToUpperInvariant();
        var walletToken = await _db.WalletTokens.FirstOrDefaultAsync(x => x.TokenReference == tokenReference, ct);

        var auth = new WalletAuthorizationEntity
        {
            Id = Guid.NewGuid(),
            WalletTokenId = walletToken?.Id,
            TokenReference = tokenReference,
            ClientTransactionId = request.ClientTransactionId.Trim(),
            AccountId = walletToken?.AccountId,
            CardId = walletToken?.CardId,
            Provider = walletToken?.Provider ?? "UNKNOWN",
            MerchantId = request.MerchantId?.Trim(),
            MerchantCategory = request.MerchantCategory?.Trim(),
            Amount = Math.Abs(request.Amount),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "USD" : request.CurrencyCode.Trim().ToUpperInvariant(),
            DeviceAuthenticated = request.DeviceAuthenticated,
            AuthorizedOn = postedOn,
            TraceId = Activity.Current?.TraceId.ToString()
        };

        if (walletToken is null)
            return await SaveDeclineAsync(auth, "14", "TOKEN_NOT_FOUND", ct);

        if (walletToken.Status != WalletTokenStatus.Active)
            return await SaveDeclineAsync(auth, "62", "TOKEN_NOT_ACTIVE", ct);

        var card = await _db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == walletToken.CardId, ct);
        if (card is null || card.Status != CardStatus.Active)
            return await SaveDeclineAsync(auth, "62", "CARD_NOT_ACTIVE", ct);

        if (!request.DeviceAuthenticated)
            return await SaveDeclineAsync(auth, "A1", "DEVICE_AUTH_REQUIRED", ct);

        var stan = DigitsFromHash(auth.ClientTransactionId, 6);
        var rrn = DigitsFromHash($"{auth.ClientTransactionId}:rrn", 12);

        try
        {
            var hold = await _holds.AuthorizeAsync(
                walletToken.AccountId,
                walletToken.CardId,
                walletToken.Provider,
                "0100",
                stan,
                rrn,
                null,
                auth.MerchantId,
                auth.MerchantCategory,
                request.CountryCode,
                null,
                auth.Amount,
                postedOn,
                ct);

            auth.Status = WalletAuthorizationStatus.Approved;
            auth.ResponseCode = "00";
            auth.Reason = "APPROVED";
            auth.HoldId = hold.Id;
            walletToken.LastUsedOn = DateTimeOffset.UtcNow;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("AUTH_DECLINED:", StringComparison.OrdinalIgnoreCase))
        {
            auth.Status = WalletAuthorizationStatus.Declined;
            auth.ResponseCode = "05";
            auth.Reason = ex.Message["AUTH_DECLINED:".Length..];
        }

        _db.WalletAuthorizations.Add(auth);
        await _db.SaveChangesAsync(ct);
        await PublishAuthorizationAsync(auth, ct);
        return Map(auth);
    }

    private async Task<WalletAuthorizationView> SaveDeclineAsync(WalletAuthorizationEntity auth, string responseCode, string reason, CancellationToken ct)
    {
        auth.Status = WalletAuthorizationStatus.Declined;
        auth.ResponseCode = responseCode;
        auth.Reason = reason;

        _db.WalletAuthorizations.Add(auth);
        await _db.SaveChangesAsync(ct);
        await PublishAuthorizationAsync(auth, ct);
        return Map(auth);
    }

    private async Task PublishTokenStateAsync(string eventType, WalletTokenEntity entity, CancellationToken ct)
    {
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = WalletTokenTopic,
            Key = entity.Id.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                walletTokenId = entity.Id,
                cardId = entity.CardId,
                accountId = entity.AccountId,
                customerId = entity.CustomerId,
                provider = entity.Provider,
                tokenReference = entity.TokenReference,
                status = entity.Status.ToString(),
                activatedOn = entity.ActivatedOn,
                traceId = Activity.Current?.TraceId.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(eventType, new
        {
            walletTokenId = entity.Id,
            cardId = entity.CardId,
            accountId = entity.AccountId,
            customerId = entity.CustomerId,
            provider = entity.Provider,
            tokenReference = entity.TokenReference,
            status = entity.Status.ToString()
        }, entity.Id.ToString("N"), Activity.Current?.TraceId.ToString(), ct);
    }

    private async Task PublishAuthorizationAsync(WalletAuthorizationEntity entity, CancellationToken ct)
    {
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = WalletAuthorizationTopic,
            Key = entity.ClientTransactionId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                authorizationId = entity.Id,
                walletTokenId = entity.WalletTokenId,
                tokenReference = entity.TokenReference,
                clientTransactionId = entity.ClientTransactionId,
                accountId = entity.AccountId,
                cardId = entity.CardId,
                provider = entity.Provider,
                amount = entity.Amount,
                currencyCode = entity.CurrencyCode,
                status = entity.Status.ToString(),
                responseCode = entity.ResponseCode,
                reason = entity.Reason,
                holdId = entity.HoldId,
                traceId = entity.TraceId
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("wallet.authorization.completed", new
        {
            authorizationId = entity.Id,
            walletTokenId = entity.WalletTokenId,
            tokenReference = entity.TokenReference,
            clientTransactionId = entity.ClientTransactionId,
            accountId = entity.AccountId,
            cardId = entity.CardId,
            provider = entity.Provider,
            amount = entity.Amount,
            currencyCode = entity.CurrencyCode,
            status = entity.Status.ToString(),
            responseCode = entity.ResponseCode,
            reason = entity.Reason,
            holdId = entity.HoldId
        }, entity.ClientTransactionId, entity.TraceId, ct);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string DigitsFromHash(string value, int length)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(length);
        foreach (var b in bytes)
        {
            builder.Append((b % 10).ToString());
            if (builder.Length >= length)
                break;
        }

        while (builder.Length < length)
            builder.Append('0');

        return builder.ToString();
    }

    private static WalletTokenView Map(WalletTokenEntity entity)
        => new(entity.Id, entity.CardId, entity.AccountId, entity.CustomerId, entity.Provider, entity.DeviceReference, entity.WalletReference, entity.TokenReference, entity.AuthenticationMethod, entity.Status.ToString(), entity.ActivationHint, entity.ActivationExpiresOn, entity.ActivatedOn, entity.LastUsedOn, entity.CreatedOn);

    private static WalletAuthorizationView Map(WalletAuthorizationEntity entity)
        => new(entity.Id, entity.ClientTransactionId, entity.TokenReference, entity.AccountId, entity.CardId, entity.Provider, decimal.Round(entity.Amount, 2), entity.CurrencyCode, entity.Status.ToString(), entity.ResponseCode, entity.Reason, entity.HoldId, entity.AuthorizedOn);
}
