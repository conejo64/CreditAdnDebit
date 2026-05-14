using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class CardProductCacheEntity
{
    [Key]
    public Guid Id { get; set; }

    public string Code { get; set; } = default!;
    public string Brand { get; set; } = default!;
    public string ProductType { get; set; } = default!;
    public string Name { get; set; } = default!;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}