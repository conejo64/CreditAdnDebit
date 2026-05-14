using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Wallets;

public enum WalletAuthorizationStatus
{
    Approved = 1,
    Declined = 2
}

public sealed class WalletAuthorizationEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid? WalletTokenId { get; set; }
    public WalletTokenEntity? WalletToken { get; set; }

    [MaxLength(64)]
    public string TokenReference { get; set; } = default!;

    [MaxLength(80)]
    public string ClientTransactionId { get; set; } = default!;

    public Guid? AccountId { get; set; }

    public Guid? CardId { get; set; }

    [MaxLength(30)]
    public string Provider { get; set; } = default!;

    [MaxLength(32)]
    public string? MerchantId { get; set; }

    [MaxLength(8)]
    public string? MerchantCategory { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    public bool DeviceAuthenticated { get; set; }

    public WalletAuthorizationStatus Status { get; set; }

    [MaxLength(4)]
    public string ResponseCode { get; set; } = "05";

    [MaxLength(120)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? TraceId { get; set; }

    public Guid? HoldId { get; set; }

    public DateTimeOffset AuthorizedOn { get; set; } = DateTimeOffset.UtcNow;
}
