using CardVault.Infrastructure.Persistence.Catalog;

namespace CardVault.Infrastructure.Persistence.Switch;

public enum AntifraudRuleType
{
    BlockCountry = 1,
    MonitorCountry = 2,
    BlockMerchant = 3,
    RiskScoreMultiplier = 4,
    VelocityPerCard = 5
}

public sealed class AntifraudRuleEntity
{
    public Guid Id { get; set; }
    public AntifraudRuleType Type { get; set; }
    
    // Target (Country Code, Merchant ID, MCC, etc.)
    public string TargetValue { get; set; } = string.Empty;
    
    public decimal RiskScore { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    public string? Description { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}
