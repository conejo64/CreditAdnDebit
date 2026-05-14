namespace CardVault.Infrastructure.Persistence.Vault;

/// <summary>
/// Singleton settings row for the vault (active key + job state).
/// </summary>
public sealed class VaultSettingsEntity
{
    public Guid Id { get; set; }

    public string ActiveKeyId { get; set; } = "k1";

    public DateTimeOffset UpdatedOn { get; set; }

    // Re-encrypt job state
    public DateTimeOffset? LastReencryptRunOn { get; set; }
    public int LastReencryptUpdated { get; set; }
    public string? LastReencryptStatus { get; set; }
}