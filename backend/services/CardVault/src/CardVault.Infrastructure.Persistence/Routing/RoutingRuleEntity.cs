using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Routing;

public sealed class RoutingRuleEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Priority { get; set; } = 100;

    public int BinStart { get; set; }
    public int BinEnd { get; set; }

    public string? Country { get; set; }
    public string? Mcc { get; set; }
    public string? MerchantId { get; set; }

    public decimal? AmountMin { get; set; }
    public decimal? AmountMax { get; set; }

    public string ConnectorId { get; set; } = default!;
    public bool Enabled { get; set; } = true;

    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}