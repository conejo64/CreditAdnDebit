namespace CardVault.Infrastructure.Persistence.Vault;

public sealed class TokenVaultEntryEntity
{
    public Guid Id { get; set; }
    public string Token { get; set; } = default!;
    public string KeyId { get; set; } = default!;
    public string NonceB64 { get; set; } = default!;
    public string CiphertextB64 { get; set; } = default!;
    public string TagB64 { get; set; } = default!;
    public string? MaskedPan { get; set; }
    public string? Bin { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset? LastAccessedOn { get; set; }
}