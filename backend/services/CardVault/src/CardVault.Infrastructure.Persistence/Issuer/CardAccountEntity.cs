using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Issuer;

public enum AccountType
{
    Debit = 1,
    Credit = 2
}

public enum AccountStatus
{
    Active = 1,
    Blocked = 2,
    Closed = 3,
    Delinquent = 4
}

public sealed class CardAccountEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public CustomerEntity Customer { get; set; } = default!;

    [MaxLength(20)]
    public string AccountNumber { get; set; } = default!;

    public AccountType AccountType { get; set; }

    [MaxLength(3)]
    public string CurrencyCode { get; set; } = "USD";

    [MaxLength(64)]
    public string ProductCode { get; set; } = default!;

    public decimal CreditLimit { get; set; } // for credit
    public decimal AvailableLimit { get; set; }
    public decimal LedgerBalance { get; set; }
    public decimal HoldBalance { get; set; }

    public AccountStatus Status { get; set; } = AccountStatus.Active;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<CardEntity> Cards { get; set; } = new();
}
