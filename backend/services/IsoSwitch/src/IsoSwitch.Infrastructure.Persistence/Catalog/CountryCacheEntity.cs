using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Infrastructure.Persistence.Catalog;

public sealed class CountryCacheEntity
{
    [Key]
    [MaxLength(2)]
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string NumericCode { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}