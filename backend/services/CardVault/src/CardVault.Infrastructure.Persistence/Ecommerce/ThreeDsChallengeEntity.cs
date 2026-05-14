using System.ComponentModel.DataAnnotations;
using CardVault.Infrastructure.Persistence.Issuer;

namespace CardVault.Infrastructure.Persistence.Ecommerce;

public enum ThreeDsChallengeStatus
{
    Pending = 1,
    Authenticated = 2,
    Rejected = 3,
    Expired = 4
}

public enum ThreeDsDecision
{
    Pending = 1,
    Approve = 2,
    Reject = 3
}

public sealed class ThreeDsChallengeEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CardId { get; set; }
    public CardEntity Card { get; set; } = default!;

    public Guid AccountId { get; set; }

    public Guid CustomerId { get; set; }

    [MaxLength(19)]
    public string MaskedPan { get; set; } = default!;

    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string CurrencyCode { get; set; } = "USD";

    [MaxLength(32)]
    public string MerchantId { get; set; } = default!;

    [MaxLength(120)]
    public string MerchantName { get; set; } = default!;

    [MaxLength(2)]
    public string? MerchantCountry { get; set; }

    [MaxLength(2)]
    public string? BrowserIpCountry { get; set; }

    [MaxLength(24)]
    public string DeviceChannel { get; set; } = "BROWSER";

    public int RiskScore { get; set; }

    public string RiskReasonsJson { get; set; } = "[]";

    [MaxLength(120)]
    public string ContactHint { get; set; } = default!;

    [MaxLength(128)]
    public string OtpHash { get; set; } = default!;

    [MaxLength(64)]
    public string OtpSalt { get; set; } = default!;

    public int OtpAttempts { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public ThreeDsChallengeStatus Status { get; set; } = ThreeDsChallengeStatus.Pending;

    public ThreeDsDecision Decision { get; set; } = ThreeDsDecision.Pending;

    [MaxLength(64)]
    public string? DecisionReason { get; set; }

    [MaxLength(120)]
    public string RequestedBy { get; set; } = default!;

    [MaxLength(64)]
    public string TraceId { get; set; } = default!;

    public DateTimeOffset ExpiresOn { get; set; }

    public DateTimeOffset? AuthenticatedOn { get; set; }

    public DateTimeOffset? CompletedOn { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}
