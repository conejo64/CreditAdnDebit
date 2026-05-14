using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Billing;

public enum HoldStatus
{
    Active = 1,
    Captured = 2,
    PartiallyCaptured = 3,
    Released = 4,
    Expired = 5
}

/// <summary>
/// Represents an authorization hold (pre-auth) that can later be captured (clearing) or released (reversal).
/// Matching uses STAN/RRN and optional OriginalDataElements (ISO8583 field 90).
/// </summary>
public sealed class AuthorizationHoldEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    [MaxLength(32)]
    public string Network { get; set; } = "Visa";

    [MaxLength(8)]
    public string Stan { get; set; } = "000000";

    [MaxLength(32)]
    public string Rrn { get; set; } = "";

    /// <summary>ISO8583 Field 90 (Original Data Elements) - simplified.</summary>
    [MaxLength(64)]
    public string? OriginalDataElements90 { get; set; }

    public decimal Amount { get; set; }

    public decimal CapturedAmount { get; set; } = 0m;

    [MaxLength(32)]
    public string? MerchantId { get; set; }

    [MaxLength(8)]
    public string? MerchantCategory { get; set; } // MCC

    public HoldStatus Status { get; set; } = HoldStatus.Active;

    public DateTimeOffset AuthorizedOn { get; set; }

    public DateTimeOffset ExpiresOn { get; set; }

    public DateTimeOffset? CapturedOn { get; set; }

    public DateTimeOffset? ReleasedOn { get; set; }

    public Guid? HoldLedgerEntryId { get; set; }

    public Guid? CaptureLedgerEntryId { get; set; }
}
