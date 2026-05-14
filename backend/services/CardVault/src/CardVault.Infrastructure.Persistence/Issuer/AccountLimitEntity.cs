using System.ComponentModel.DataAnnotations;

namespace CardVault.Infrastructure.Persistence.Issuer;

public sealed class AccountLimitEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }
    
    public decimal DailyAtmLimit { get; set; }
    public decimal DailyPosLimit { get; set; }
    public decimal DailyEcommerceLimit { get; set; }
    
    public decimal DailyAtmAuculated { get; set; }
    public decimal DailyPosAccumulated { get; set; }
    public decimal DailyEcommerceAccumulated { get; set; }

    public DateTime LastResetDate { get; set; } = DateTime.UtcNow.Date;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
