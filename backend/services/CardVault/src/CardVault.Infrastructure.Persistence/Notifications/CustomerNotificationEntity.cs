using System.ComponentModel.DataAnnotations;
using CardVault.Infrastructure.Persistence.Issuer;

namespace CardVault.Infrastructure.Persistence.Notifications;

public enum CustomerNotificationType
{
    Transaction = 1,
    SecurityAlert = 2
}

public enum NotificationSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public sealed class CustomerNotificationEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public CustomerEntity Customer { get; set; } = default!;

    public Guid? AccountId { get; set; }

    public Guid? CardId { get; set; }

    public CustomerNotificationType Type { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    [MaxLength(140)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Message { get; set; } = default!;

    public decimal? Amount { get; set; }

    [MaxLength(3)]
    public string? CurrencyCode { get; set; }

    [MaxLength(120)]
    public string? MerchantName { get; set; }

    [MaxLength(64)]
    public string? SourceEvent { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReadOn { get; set; }

    public List<CustomerNotificationDeliveryEntity> Deliveries { get; set; } = new();
}

public enum NotificationChannel
{
    Email = 1,
    Sms = 2
}

public enum NotificationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public sealed class CustomerNotificationDeliveryEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid NotificationId { get; set; }
    public CustomerNotificationEntity Notification { get; set; } = default!;

    public NotificationChannel Channel { get; set; }

    [MaxLength(120)]
    public string DestinationMasked { get; set; } = default!;

    [MaxLength(256)]
    public string DestinationHash { get; set; } = default!;

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

    public int Attempts { get; set; }

    [MaxLength(128)]
    public string? ProviderReference { get; set; }

    [MaxLength(256)]
    public string? LastError { get; set; }

    public DateTimeOffset? LastAttemptOn { get; set; }

    public DateTimeOffset? DeliveredOn { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
