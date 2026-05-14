using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Catalog;

public sealed class CardProductEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = default!; // e.g., VISA_CREDIT_CLASSIC
    public string Brand { get; set; } = default!;
    public string ProductType { get; set; } = default!; // CREDIT/DEBIT
    public string Name { get; set; } = default!;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}