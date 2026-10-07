using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace BuildingBlocks.Security;

/// <summary>
/// PEM pair for an ES256 key: PKCS#8 private key and SubjectPublicKeyInfo public key.
/// </summary>
public sealed record EcdsaKeyPairPem(string PrivateKeyPem, string PublicKeyPem);

/// <summary>
/// Public JSON Web Key (RFC 7517) for an ES256 key. It never carries the private scalar.
/// </summary>
public sealed record EcdsaPublicJwk(string Kty, string Crv, string X, string Y, string Kid, string Use, string Alg);

/// <summary>
/// Raised when key material cannot be used as an ES256 key. The message is written for an
/// operator reading a startup failure, so it names what was wrong, never the key bytes.
/// </summary>
public sealed class EcdsaKeyMaterialException : Exception
{
    public EcdsaKeyMaterialException(string message) : base(message) { }
    public EcdsaKeyMaterialException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// G1-T5: loads and describes the ES256 (ECDSA P-256) key material used to sign and verify
/// JWTs across services. CardVault holds the private key; every service holds the public key.
/// This helper depends on the BCL only so the three services and their tests share one
/// definition of "a valid key" and one <c>kid</c> derivation.
/// </summary>
public static class EcdsaKeyMaterial
{
    public const string Algorithm = "ES256";
    public const string CurveName = "P-256";
    public const string PublicKeyPemLabel = "-----BEGIN PUBLIC KEY-----";

    private const string PrivateKeyMarker = "PRIVATE KEY";
    private const int P256KeySizeBits = 256;
    private const int CoordinateLength = 32;

    /// <summary>
    /// Accepts the PEM as operators realistically supply it: a multi-line block, or a single
    /// line with "\n" escape sequences (Docker .env files cannot hold multi-line values).
    /// </summary>
    public static string NormalizePem(string? pem)
        => (pem ?? string.Empty).Replace("\\n", "\n").Trim();

    public static bool ContainsPrivateKey(string? pem)
        => !string.IsNullOrEmpty(pem) && pem.Contains(PrivateKeyMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves PEM text from an inline setting or, failing that, from a file path.
    /// Returns <c>null</c> when neither is configured.
    /// </summary>
    public static string? ResolvePem(string? inlinePem, string? path, string pathSettingName)
    {
        if (!string.IsNullOrWhiteSpace(inlinePem))
            return NormalizePem(inlinePem);

        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return NormalizePem(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new EcdsaKeyMaterialException($"{pathSettingName} points to '{path}', which could not be read ({ex.Message}).", ex);
        }
    }

    /// <summary>Imports a P-256 public key from a SubjectPublicKeyInfo PEM block.</summary>
    public static ECDsa ImportPublicKey(string? pem)
    {
        var normalized = NormalizePem(pem);
        if (normalized.Length == 0)
            throw new EcdsaKeyMaterialException("the PEM is empty.");
        if (ContainsPrivateKey(normalized))
            throw new EcdsaKeyMaterialException($"the PEM contains a private key; only the public key ('{PublicKeyPemLabel}') is accepted here.");
        if (!normalized.Contains(PublicKeyPemLabel, StringComparison.Ordinal))
            throw new EcdsaKeyMaterialException($"the PEM does not contain a '{PublicKeyPemLabel}' block.");

        return Import(normalized, "public");
    }

    /// <summary>Imports a P-256 private key from a PKCS#8 or SEC1 PEM block.</summary>
    public static ECDsa ImportPrivateKey(string? pem)
    {
        var normalized = NormalizePem(pem);
        if (normalized.Length == 0)
            throw new EcdsaKeyMaterialException("the PEM is empty.");
        if (!ContainsPrivateKey(normalized))
            throw new EcdsaKeyMaterialException("the PEM does not contain a private key ('-----BEGIN PRIVATE KEY-----' or '-----BEGIN EC PRIVATE KEY-----').");

        return Import(normalized, "private");
    }

    private static ECDsa Import(string pem, string kind)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key.Dispose();
            throw new EcdsaKeyMaterialException($"the PEM is not a valid EC {kind} key ({ex.Message}).", ex);
        }

        if (key.KeySize != P256KeySizeBits)
        {
            var size = key.KeySize;
            key.Dispose();
            throw new EcdsaKeyMaterialException($"the key uses a {size}-bit curve; {Algorithm} requires {CurveName}.");
        }

        return key;
    }

    /// <summary>
    /// RFC 7638 JWK thumbprint (SHA-256, base64url) of the public key. Deterministic for a
    /// given key, so rotation shows up as a different <c>kid</c> and nothing else.
    /// </summary>
    public static string ComputeKeyId(ECDsa key)
    {
        var (x, y) = PublicCoordinates(key);
        var canonical = $"{{\"crv\":\"{CurveName}\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static EcdsaPublicJwk ToPublicJwk(ECDsa key)
    {
        var (x, y) = PublicCoordinates(key);
        return new EcdsaPublicJwk("EC", CurveName, x, y, ComputeKeyId(key), "sig", Algorithm);
    }

    /// <summary>True when <paramref name="publicKey"/> is the public half of <paramref name="privateKey"/>.</summary>
    public static bool PublicKeyMatches(ECDsa privateKey, ECDsa publicKey)
        => privateKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo());

    public static string ExportPublicKeyPem(ECDsa key) => key.ExportSubjectPublicKeyInfoPem();

    /// <summary>Generates a fresh P-256 pair (tests, and CardVault's explicit Development-only ephemeral mode).</summary>
    public static EcdsaKeyPairPem GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new EcdsaKeyPairPem(key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    private static (string X, string Y) PublicCoordinates(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return (Base64Url.EncodeToString(LeftPad(parameters.Q.X)), Base64Url.EncodeToString(LeftPad(parameters.Q.Y)));
    }

    // Coordinates are fixed-width in JWK; some providers strip leading zero bytes on export.
    private static byte[] LeftPad(byte[]? coordinate)
    {
        var value = coordinate ?? [];
        if (value.Length >= CoordinateLength)
            return value;

        var padded = new byte[CoordinateLength];
        value.CopyTo(padded, CoordinateLength - value.Length);
        return padded;
    }
}
