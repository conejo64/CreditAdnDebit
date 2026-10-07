using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardVault.Application.Ports;

namespace CardVault.IntegrationTests.Issuer;

/// <summary>
/// Test implementation of <see cref="IContactDataEncryptor"/> with the same wire semantics as the
/// production <c>CardVault.Api.Vault.VaultCrypto</c> (JSON payload, AES-256-GCM, 12-byte nonce,
/// 16-byte tag, Base64 parts). It lives here because this project deliberately does not reference
/// <c>CardVault.Api</c>; the key is a fixed test value and must never be used outside tests.
/// </summary>
public sealed class AesGcmTestEncryptor : IContactDataEncryptor
{
    public const string KeyId = "it-k1";

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    public (string keyId, string nonceB64, string cipherB64, string tagB64) EncryptToParts<T>(T payload)
    {
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(Key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);

        return (KeyId, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
    }

    public T DecryptFromParts<T>(string keyId, string nonceB64, string cipherB64, string tagB64)
    {
        if (keyId != KeyId) throw new InvalidOperationException($"Unknown KeyId: {keyId}");

        var cipher = Convert.FromBase64String(cipherB64);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(Key, 16);
        aes.Decrypt(Convert.FromBase64String(nonceB64), cipher, Convert.FromBase64String(tagB64), plain);

        return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(plain))
            ?? throw new InvalidOperationException("Decrypt JSON failed");
    }
}
