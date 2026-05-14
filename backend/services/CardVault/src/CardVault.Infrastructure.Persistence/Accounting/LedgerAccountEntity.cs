using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Accounting;

public sealed class LedgerAccountEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(30)]
    public string AccountCode { get; set; } = default!;

    [MaxLength(150)]
    public string AccountName { get; set; } = default!;

    [MaxLength(30)]
    public string AccountType { get; set; } = default!;

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    [MaxLength(20)]
    public string Status { get; set; } = "ACTIVE";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
