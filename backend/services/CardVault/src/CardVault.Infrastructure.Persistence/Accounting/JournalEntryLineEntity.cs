using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Accounting;

public sealed class JournalEntryLineEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid JournalEntryId { get; set; }
    public JournalEntryEntity JournalEntry { get; set; } = default!;

    public Guid LedgerAccountId { get; set; }
    public LedgerAccountEntity LedgerAccount { get; set; } = default!;

    public decimal DebitAmount { get; set; }

    public decimal CreditAmount { get; set; }

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    [MaxLength(250)]
    public string Description { get; set; } = default!;
}
