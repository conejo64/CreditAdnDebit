namespace CardVault.Application.Ports;

/// <summary>
/// Port for PAN tokenization/detokenization operations.
/// Implemented by CardVault.Api.Vault.TokenVaultService (stays in Api).
/// </summary>
public interface IPanVault
{
    Task<TokenizeResult> TokenizeAsync(string pan, string? expiryYyMm, string actor, string? traceId, CancellationToken ct);
    Task<DetokenizeResult> DetokenizeAsync(string token, string actor, string? traceId, CancellationToken ct);
    Task<TokenMetadataResult> GetMetadataAsync(string token, CancellationToken ct);
    Task<RotateKeyResult> RotateActiveKeyAsync(string newActiveKeyId, string actor, string? traceId, CancellationToken ct);
    Task<ReEncryptBatchResult> ReEncryptBatchAsync(int take, string actor, string? traceId, CancellationToken ct);
    Task<(string ActiveKeyId, IReadOnlyList<string> AvailableKeyIds)> GetActiveKeyInfoAsync(CancellationToken ct);
}

public sealed record TokenizeResult(string Token, string MaskedPan, string? Bin, string KeyId, DateTimeOffset CreatedOn);
public sealed record DetokenizeResult(string Token, string Pan, string? ExpiryYyMm, string KeyId);
public sealed record TokenMetadataResult(string Token, string? MaskedPan, string? Bin, string KeyId, DateTimeOffset CreatedOn, DateTimeOffset? LastAccessedOn);
public sealed record RotateKeyResult(string ActiveKeyId, DateTimeOffset RotatedOn, string Actor);
public sealed record ReEncryptBatchResult(string ActiveKeyId, int UpdatedCount, DateTimeOffset RotatedOn);
