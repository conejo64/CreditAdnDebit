using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class BinRangeCacheEntity
{
    [Key]
    public Guid Id { get; set; }

    public int BinStart { get; set; }
    public int BinEnd { get; set; }

    public string Brand { get; set; } = default!;
    public string Product { get; set; } = default!;
    public string? IssuerName { get; set; }
    public string? CountryCode { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}