using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Notifications;
using CardVault.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

public sealed class NotificationService
{
    private const string NotificationCreatedTopic = "cv.customer.notification.created";
    private const string NotificationDeliveredTopic = "cv.customer.notification.delivered";

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly IPciAuditPublisher _pciAudit;
    private readonly IContactDataEncryptor _crypto;

    public NotificationService(
        CardVaultDbContext db,
        AuditService audit,
        IPciAuditPublisher pciAudit,
        IContactDataEncryptor crypto)
    {
        _db = db;
        _audit = audit;
        _pciAudit = pciAudit;
        _crypto = crypto;
    }

    public async Task CreateTransactionNotificationAsync(
        Guid accountId,
        Guid? cardId,
        decimal amount,
        string currencyCode,
        string merchantName,
        string sourceEvent,
        string traceId,
        CancellationToken ct)
    {
        var target = await ResolveTargetAsync(accountId, cardId, ct);
        if (target is null)
            return;

        var resolved = target.Value;

        var normalizedMerchant = string.IsNullOrWhiteSpace(merchantName) ? "merchant unavailable" : merchantName.Trim();
        var title = "Transaction notification";
        var message = $"Movement of {decimal.Round(amount, 2, MidpointRounding.AwayFromZero):0.00} {currencyCode} at {normalizedMerchant}.";

        await CreateNotificationAsync(
            resolved.CustomerId,
            accountId,
            resolved.CardId,
            CustomerNotificationType.Transaction,
            NotificationSeverity.Info,
            title,
            message,
            amount,
            currencyCode,
            normalizedMerchant,
            sourceEvent,
            traceId,
            ct);
    }

    public async Task CreateSecurityAlertAsync(
        Guid customerId,
        Guid? accountId,
        Guid? cardId,
        string title,
        string message,
        string sourceEvent,
        string traceId,
        NotificationSeverity severity,
        CancellationToken ct)
    {
        await CreateNotificationAsync(
            customerId,
            accountId,
            cardId,
            CustomerNotificationType.SecurityAlert,
            severity,
            title,
            message,
            null,
            null,
            null,
            sourceEvent,
            traceId,
            ct);
    }

    public async Task<IReadOnlyList<CustomerNotificationView>> ListAsync(
        Guid? customerId,
        Guid? accountId,
        string? type,
        int take,
        CancellationToken ct)
    {
        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var query = _db.CustomerNotifications
            .AsNoTracking()
            .Include(x => x.Deliveries)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(x => x.CustomerId == customerId.Value);

        if (accountId.HasValue)
            query = query.Where(x => x.AccountId == accountId.Value);

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<CustomerNotificationType>(type, true, out var parsedType))
            query = query.Where(x => x.Type == parsedType);

        var items = await query
            .OrderByDescending(x => x.CreatedOn)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task<CustomerNotificationView?> GetAsync(Guid notificationId, CancellationToken ct)
    {
        var entity = await _db.CustomerNotifications
            .AsNoTracking()
            .Include(x => x.Deliveries)
            .FirstOrDefaultAsync(x => x.Id == notificationId, ct);

        return entity is null ? null : Map(entity);
    }

    private async Task CreateNotificationAsync(
        Guid customerId,
        Guid? accountId,
        Guid? cardId,
        CustomerNotificationType type,
        NotificationSeverity severity,
        string title,
        string message,
        decimal? amount,
        string? currencyCode,
        string? merchantName,
        string sourceEvent,
        string traceId,
        CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, ct);
        if (customer is null)
            return;

        var notification = new CustomerNotificationEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            AccountId = accountId,
            CardId = cardId,
            Type = type,
            Severity = severity,
            Title = title,
            Message = message,
            Amount = amount,
            CurrencyCode = currencyCode,
            MerchantName = merchantName,
            SourceEvent = sourceEvent,
            TraceId = traceId,
            CreatedOn = DateTimeOffset.UtcNow
        };

        foreach (var delivery in BuildDeliveries(notification.Id, customer))
            notification.Deliveries.Add(delivery);

        _db.CustomerNotifications.Add(notification);
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = NotificationCreatedTopic,
            Key = notification.Id.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                notificationId = notification.Id,
                customerId = notification.CustomerId,
                accountId = notification.AccountId,
                cardId = notification.CardId,
                type = notification.Type.ToString(),
                severity = notification.Severity.ToString(),
                title = notification.Title,
                message = notification.Message,
                amount = notification.Amount,
                currencyCode = notification.CurrencyCode,
                merchantName = notification.MerchantName,
                sourceEvent = notification.SourceEvent,
                traceId = notification.TraceId,
                deliveries = notification.Deliveries.Select(x => new
                {
                    channel = x.Channel.ToString(),
                    destinationMasked = x.DestinationMasked,
                    status = x.Status.ToString()
                })
            })
        });

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("cardvault.notification.created", new
        {
            notificationId = notification.Id,
            customerId = notification.CustomerId,
            accountId = notification.AccountId,
            cardId = notification.CardId,
            type = notification.Type.ToString(),
            severity = notification.Severity.ToString(),
            title = notification.Title,
            amount = notification.Amount,
            currencyCode = notification.CurrencyCode,
            merchantName = notification.MerchantName,
            sourceEvent = notification.SourceEvent,
            traceId = notification.TraceId,
            deliveries = notification.Deliveries.Select(x => new
            {
                channel = x.Channel.ToString(),
                destinationMasked = x.DestinationMasked,
                status = x.Status.ToString()
            })
        }, notification.Id.ToString("N"), traceId, ct);

        await _pciAudit.PublishAsync("pci.notification.created", notification.Id.ToString("N"), new
        {
            notificationId = notification.Id,
            customerId = notification.CustomerId,
            accountId = notification.AccountId,
            cardId = notification.CardId,
            type = notification.Type.ToString(),
            severity = notification.Severity.ToString(),
            title = notification.Title,
            amount = notification.Amount,
            currencyCode = notification.CurrencyCode,
            merchantName = notification.MerchantName,
            sourceEvent = notification.SourceEvent,
            traceId = notification.TraceId,
            deliveries = notification.Deliveries.Select(x => new
            {
                channel = x.Channel.ToString(),
                destinationMasked = x.DestinationMasked,
                status = x.Status.ToString()
            })
        }, ct);
    }

    private async Task<(Guid CustomerId, Guid? CardId)?> ResolveTargetAsync(Guid accountId, Guid? cardId, CancellationToken ct)
    {
        if (cardId.HasValue)
        {
            var card = await _db.Cards
                .Include(x => x.Account)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == cardId.Value, ct);

            if (card is not null)
                return (card.Account.CustomerId, card.Id);
        }

        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
        return account is null ? null : (account.CustomerId, cardId);
    }

    private IEnumerable<CustomerNotificationDeliveryEntity> BuildDeliveries(
        Guid notificationId,
        Infrastructure.Persistence.Issuer.CustomerEntity customer)
    {
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            var (keyId, nonce, cipher, tag) = _crypto.EncryptToParts<string>(customer.Email);
            yield return new CustomerNotificationDeliveryEntity
            {
                Id = Guid.NewGuid(),
                NotificationId = notificationId,
                Channel = NotificationChannel.Email,
                DestinationMasked = MaskEmail(customer.Email),
                DestinationHash = HashDestination(customer.Email),
                DestinationKeyId = keyId,
                DestinationNonceB64 = nonce,
                DestinationCipherB64 = cipher,
                DestinationTagB64 = tag,
                Status = NotificationDeliveryStatus.Pending,
                CreatedOn = DateTimeOffset.UtcNow
            };
        }

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            var (keyId, nonce, cipher, tag) = _crypto.EncryptToParts<string>(customer.Phone);
            yield return new CustomerNotificationDeliveryEntity
            {
                Id = Guid.NewGuid(),
                NotificationId = notificationId,
                Channel = NotificationChannel.Sms,
                DestinationMasked = MaskPhone(customer.Phone),
                DestinationHash = HashDestination(customer.Phone),
                DestinationKeyId = keyId,
                DestinationNonceB64 = nonce,
                DestinationCipherB64 = cipher,
                DestinationTagB64 = tag,
                Status = NotificationDeliveryStatus.Pending,
                CreatedOn = DateTimeOffset.UtcNow
            };
        }
    }

    private static CustomerNotificationView Map(CustomerNotificationEntity entity)
        => new(
            entity.Id,
            entity.CustomerId,
            entity.AccountId,
            entity.CardId,
            entity.Type.ToString().ToUpperInvariant(),
            entity.Severity.ToString().ToUpperInvariant(),
            entity.Title,
            entity.Message,
            entity.Amount,
            entity.CurrencyCode,
            entity.MerchantName,
            entity.SourceEvent,
            entity.TraceId,
            entity.CreatedOn,
            entity.ReadOn,
            entity.Deliveries
                .OrderBy(x => x.Channel)
                .Select(x => new NotificationDeliveryView(
                    x.Id,
                    x.Channel.ToString().ToUpperInvariant(),
                    x.DestinationMasked,
                    x.Status.ToString().ToUpperInvariant(),
                    x.Attempts,
                    x.LastAttemptOn,
                    x.DeliveredOn,
                    x.ProviderReference,
                    x.LastError))
                .ToList());

    private static string MaskEmail(string email)
    {
        if (!email.Contains('@'))
            return "e***";

        var parts = email.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
        var local = parts[0];
        var domain = parts[1];
        var prefix = local.Length <= 2 ? local[..1] : local[..2];
        return $"{prefix}***@{domain}";
    }

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length < 4 ? "***" : $"***{digits[^4..]}";
    }

    private static string HashDestination(string destination)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(destination.Trim().ToLowerInvariant()))).ToLowerInvariant();
}
