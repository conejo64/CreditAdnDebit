using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Routing;

/// <summary>
/// Routing rule with optional dimensions beyond BIN.
/// </summary>
public sealed class RoutingRuleV2Entity
{
    [Key]
    public Guid Id { get; set; }

    public int Priority { get; set; } = 100;

    public int BinStart { get; set; }
    public int BinEnd { get; set; }

    [MaxLength(2)]
    public string? CountryCode { get; set; } // optional ISO3166-1 alpha2

    [MaxLength(16)]
    public string? Network { get; set; } // VISA / MC / LOCAL

    [MaxLength(16)]
    public string? TxType { get; set; } // AUTH / CAPTURE / REVERSAL

    [MaxLength(64)]
    public string ConnectorId { get; set; } = default!;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}