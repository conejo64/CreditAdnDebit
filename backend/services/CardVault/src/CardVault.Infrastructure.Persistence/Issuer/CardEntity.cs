using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Issuer;

public enum CardStatus
{
    Created = 1,
    Personalized = 2,
    Printed = 3,
    Delivered = 4,
    Active = 5,
    Blocked = 6,
    Cancelled = 7,
    Expired = 8
}

public sealed class CardEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }
    public CardAccountEntity Account { get; set; } = default!;

    [MaxLength(12)]
    public string Bin { get; set; } = default!;

    [MaxLength(32)]
    public string PanToken { get; set; } = default!; // token in vault

    [MaxLength(19)]
    public string MaskedPan { get; set; } = default!;

    [MaxLength(6)]
    public string ExpiryYyMm { get; set; } = default!;

    [MaxLength(8)]
    public string Last4 { get; set; } = default!;

    public CardStatus Status { get; set; } = CardStatus.Created;

    // v58 - Security (PIN)
    [MaxLength(128)]
    public string? PinHash { get; set; }
    public int PinRetryCount { get; set; }
    public DateTimeOffset? PinBlockedUntil { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<CardStatusHistoryEntity> History { get; set; } = new();
}
