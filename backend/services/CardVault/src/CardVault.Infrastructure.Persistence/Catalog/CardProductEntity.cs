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

    /// <summary>
    /// APR applied to installment plans when the request does not supply one (0.12 = 12 %).
    /// Null means the product has no default and every plan must state its APR explicitly.
    /// </summary>
    public decimal? DefaultInstallmentApr { get; set; }

    /// <summary>
    /// Upper bound for any installment APR on this product. Null means no cap is enforced.
    /// Hook for regulatory rate tables; the table itself is out of scope.
    /// </summary>
    public decimal? MaxInstallmentApr { get; set; }
    public DateTimeOffset UpdatedOn { get; set; } = DateTimeOffset.UtcNow;
}