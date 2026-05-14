using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Wallets;

public enum WalletTokenStatus
{
    PendingActivation = 1,
    Active = 2,
    Suspended = 3,
    Revoked = 4,
    Expired = 5
}

public sealed class WalletTokenEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CardId { get; set; }

    public Guid AccountId { get; set; }

    public Guid CustomerId { get; set; }

    [MaxLength(30)]
    public string Provider { get; set; } = default!;

    [MaxLength(120)]
    public string DeviceReference { get; set; } = default!;

    [MaxLength(120)]
    public string? WalletReference { get; set; }

    [MaxLength(64)]
    public string TokenReference { get; set; } = default!;

    [MaxLength(30)]
    public string AuthenticationMethod { get; set; } = default!;

    public WalletTokenStatus Status { get; set; } = WalletTokenStatus.PendingActivation;

    [MaxLength(64)]
    public string? ActivationCodeHash { get; set; }

    [MaxLength(12)]
    public string? ActivationHint { get; set; }

    public DateTimeOffset? ActivationExpiresOn { get; set; }

    public DateTimeOffset? ActivatedOn { get; set; }

    public DateTimeOffset? LastUsedOn { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<WalletAuthorizationEntity> Authorizations { get; set; } = new();
}
