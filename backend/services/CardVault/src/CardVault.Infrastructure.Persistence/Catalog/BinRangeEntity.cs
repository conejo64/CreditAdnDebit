using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Catalog;

public sealed class BinRangeEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int BinStart { get; set; }
    public int BinEnd { get; set; }

    /// <summary>Network brand: VISA, MASTERCARD, AMEX, etc.</summary>
    public string Brand { get; set; } = default!;

    /// <summary>Product: CREDIT, DEBIT, PREPAID, etc.</summary>
    public string Product { get; set; } = default!;

    public string? IssuerName { get; set; }
    public string? CountryCode { get; set; } // ISO 3166-1 alpha-2

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}