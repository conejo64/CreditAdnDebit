using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Issuer;

public sealed class CustomerEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(32)]
    public string CustomerNumber { get; set; } = default!;

    [MaxLength(120)]
    public string FullName { get; set; } = default!;

    [MaxLength(20)]
    public string DocumentId { get; set; } = default!; // cedula/pasaporte/ruc

    [MaxLength(80)]
    public string Email { get; set; } = default!;

    [MaxLength(20)]
    public string Phone { get; set; } = default!;
    [MaxLength(20)]
    public string DocumentType { get; set; } = "CEDULA";

    [MaxLength(20)]
    public string Gender { get; set; } = "N/A";

    [MaxLength(200)]
    public string BillingAddress { get; set; } = "N/A";

    [MaxLength(200)]
    public string StatementAddress { get; set; } = "N/A";

    [MaxLength(100)]
    public string ResidenceCity { get; set; } = "N/A";

    [MaxLength(100)]
    public string StatementCity { get; set; } = "N/A";

    [MaxLength(100)]
    public string CardDeliveryCity { get; set; } = "N/A";

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<CardAccountEntity> Accounts { get; set; } = new();
}
