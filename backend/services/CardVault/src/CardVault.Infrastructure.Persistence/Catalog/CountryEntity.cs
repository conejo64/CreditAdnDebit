using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Catalog;

public sealed class CountryEntity
{
    [Key]
    [MaxLength(2)]
    public string Code { get; set; } = default!; // ISO alpha-2, e.g., EC

    [MaxLength(80)]
    public string Name { get; set; } = default!;

    [MaxLength(3)]
    public string NumericCode { get; set; } = default!; // ISO numeric, e.g., 218

    [MaxLength(3)]
    public string Currency { get; set; } = default!; // ISO 4217, e.g., USD

    public bool Enabled { get; set; } = true;
}