namespace CardVault.Api.Contracts;

public sealed record NotificationDeliveryView(
    Guid DeliveryId,
    string Channel,
    string DestinationMasked,
    string Status,
    int Attempts,
    DateTimeOffset? LastAttemptOn,
    DateTimeOffset? DeliveredOn,
    string? ProviderReference,
    string? LastError);

public sealed record CustomerNotificationView(
    Guid NotificationId,
    Guid CustomerId,
    Guid? AccountId,
    Guid? CardId,
    string Type,
    string Severity,
    string Title,
    string Message,
    decimal? Amount,
    string? CurrencyCode,
    string? MerchantName,
    string? SourceEvent,
    string? TraceId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ReadOn,
    IReadOnlyList<NotificationDeliveryView> Deliveries);
