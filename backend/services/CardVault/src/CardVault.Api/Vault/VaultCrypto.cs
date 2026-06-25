using CardVault.Application.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardVault.Api.Vault;

public sealed class VaultCrypto : IContactDataEncryptor
{
    private readonly VaultOptions _opt;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public VaultCrypto(VaultOptions opt)
    {
        _opt = opt;

        // Backward compatible: PrimaryKeyB64
        if (_opt.Keys.Count == 0 && !string.IsNullOrWhiteSpace(_opt.PrimaryKeyB64))
        {
            _opt.Keys[_opt.PrimaryKeyId] = _opt.PrimaryKeyB64;
            _opt.ActiveKeyId = _opt.PrimaryKeyId;
        }

        if (_opt.Keys.Count == 0)
            throw new InvalidOperationException("Vault keys are required (Vault:Keys)");

        if (string.IsNullOrWhiteSpace(_opt.ActiveKeyId))
            _opt.ActiveKeyId = _opt.Keys.Keys.First();

        _keys = _opt.Keys.ToDictionary(k => k.Key, k =>
        {
            var b = Convert.FromBase64String(k.Value);
            if (b.Length != 32) throw new InvalidOperationException($"Vault key {k.Key} must be 32 bytes (AES-256-GCM)");
            return b;
        });
    }

    public (string keyId, string nonceB64, string cipherB64, string tagB64) EncryptToParts<T>(T payload)
    {
        var keyId = _opt.ActiveKeyId;
        var key = _keys[keyId];

        var json = JsonSerializer.Serialize(payload);
        var plain = Encoding.UTF8.GetBytes(json);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, plain, cipher, tag, associatedData: null);

        return (keyId, Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
    }

    public T DecryptFromParts<T>(string keyId, string nonceB64, string cipherB64, string tagB64)
    {
        if (!_keys.TryGetValue(keyId, out var key))
            throw new InvalidOperationException($"Unknown KeyId: {keyId}");

        var nonce = Convert.FromBase64String(nonceB64);
        var cipher = Convert.FromBase64String(cipherB64);
        var tag = Convert.FromBase64String(tagB64);

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, cipher, tag, plain, associatedData: null);

        var json = Encoding.UTF8.GetString(plain);
        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("Decrypt JSON failed");
    }

    public string ActiveKeyId => _opt.ActiveKeyId;

    public void SetActiveKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) throw new InvalidOperationException("keyId required");
        if (!_keys.ContainsKey(keyId)) throw new InvalidOperationException($"Unknown KeyId: {keyId}");
        _opt.ActiveKeyId = keyId;
    }
}


public sealed class AdminRateLimitOptions
{
    /// <summary>Maximum requests allowed in the sliding window. Dev default: 20 (relaxed).</summary>
    public int PermitLimit { get; set; } = 20;

    /// <summary>Window length in seconds. Dev default: 60 s.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Queue depth. 0 = surface 429 immediately; no queuing.</summary>
    public int QueueLimit { get; set; } = 0;
}

public sealed class VaultOptions
{
    // New v19: multiple keys for rotation
    public string ActiveKeyId { get; set; } = "k1";
    public Dictionary<string, string> Keys { get; set; } = new();

    // Back-compat (v18)
    public string PrimaryKeyId { get; set; } = "k1";
    public string PrimaryKeyB64 { get; set; } = "";

    /// <summary>Rate-limit policy for admin vault operations (rotate, re-encrypt).</summary>
    public AdminRateLimitOptions AdminRateLimit { get; set; } = new();
}