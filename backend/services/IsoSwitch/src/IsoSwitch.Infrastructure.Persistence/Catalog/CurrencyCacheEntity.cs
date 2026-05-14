using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class CurrencyCacheEntity
{
    [Key]
    [MaxLength(3)]
    public string Code { get; set; } = default!; // ISO 4217 (e.g., USD)

    public string Name { get; set; } = default!;
    public int Exponent { get; set; } = 2; // cents

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}