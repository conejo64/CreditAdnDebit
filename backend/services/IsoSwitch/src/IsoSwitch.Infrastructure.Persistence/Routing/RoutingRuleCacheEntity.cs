using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Routing;

public sealed class RoutingRuleCacheEntity
{
    [Key]
    public Guid Id { get; set; }

    public int Priority { get; set; }
    public int BinStart { get; set; }
    public int BinEnd { get; set; }
    public string ConnectorId { get; set; } = default!;
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedOn { get; set; }
}