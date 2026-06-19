namespace CardVault.Application.Ports;

/// <summary>
/// Port for encrypting and decrypting sensitive contact data (email, phone) for storage.
/// Implemented by CardVault.Api.Vault.VaultCrypto (stays in Api).
/// Returns (keyId, nonceB64, cipherB64, tagB64) tuple matching VaultCrypto.EncryptToParts contract.
/// </summary>
public interface IContactDataEncryptor
{
    (string keyId, string nonceB64, string cipherB64, string tagB64) EncryptToParts<T>(T payload);

    /// <summary>
    /// Decrypts a value previously encrypted with <see cref="EncryptToParts{T}"/>.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> on tampered or key-retired data.
    /// </summary>
    T DecryptFromParts<T>(string keyId, string nonceB64, string cipherB64, string tagB64);
}
