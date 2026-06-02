using CardVault.Api.Vault;

namespace CardVault.Tests.Infrastructure;

/// <summary>
/// Factory for creating a <see cref="VaultCrypto"/> instance suitable for unit tests.
/// Uses a deterministic all-zero 32-byte AES-256 key — NOT for production use.
/// </summary>
public static class TestVaultCrypto
{
    /// <summary>Creates a VaultCrypto with a fixed test key (32 zero-bytes, keyId = "test-k1").</summary>
    public static VaultCrypto Create()
    {
        var opts = new VaultOptions
        {
            ActiveKeyId = "test-k1",
            Keys = new Dictionary<string, string>
            {
                ["test-k1"] = Convert.ToBase64String(new byte[32])
            }
        };
        return new VaultCrypto(opts);
    }
}
