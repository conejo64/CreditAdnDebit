using CardVault.Domain;
using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Issuer;

public sealed class CardStatusHistoryEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CardId { get; set; }
    public CardEntity Card { get; set; } = default!;

    public CardStatus FromStatus { get; set; }
    public CardStatus ToStatus { get; set; }

    [MaxLength(120)]
    public string Reason { get; set; } = default!;

    public DateTimeOffset ChangedOn { get; set; } = DateTimeOffset.UtcNow;
}
