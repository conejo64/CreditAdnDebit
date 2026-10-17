using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Security;

/// <summary>
/// G1-T5: the startup rules every service applies to its <c>Jwt</c> section. Each rule returns
/// an operator-facing failure message or <c>null</c>; the per-service IValidateOptions turns
/// that into ValidateOptionsResult so the host refuses to start (ValidateOnStart).
/// </summary>
public static class JwtKeyConfigurationRules
{
    public const string Section = "Jwt";
    public const string RunbookPath = "docs/runbooks/jwt-asymmetric-keys.md";

    /// <summary>Settings of the retired shared HS256 secret. Their presence is a misconfiguration.</summary>
    public static readonly string[] RetiredSymmetricSettings = ["Jwt:SigningKey", "Jwt:Key"];

    /// <summary>Settings only the issuer (CardVault) may hold.</summary>
    public static readonly string[] IssuerOnlySettings = ["Jwt:PrivateKeyPem", "Jwt:PrivateKeyPath"];

    public static string? RejectRetiredSymmetricSettings(IConfiguration configuration)
    {
        foreach (var setting in RetiredSymmetricSettings)
        {
            if (!string.IsNullOrWhiteSpace(configuration[setting]))
                return $"{setting} is no longer supported: JWTs are signed with an {EcdsaKeyMaterial.Algorithm} key pair. " +
                       $"Remove it and configure Jwt:PublicKeyPem (validators) or Jwt:PrivateKeyPem (CardVault). See {RunbookPath}.";
        }

        return null;
    }

    public static string? RejectIssuerOnlySettings(IConfiguration configuration, string serviceName)
    {
        foreach (var setting in IssuerOnlySettings)
        {
            if (!string.IsNullOrWhiteSpace(configuration[setting]))
                return $"{serviceName} only validates tokens and must never hold the signing key: remove {setting}. " +
                       $"Only CardVault is configured with the private key. See {RunbookPath}.";
        }

        return null;
    }

    public static string? ValidateIssuerAndAudience(string? issuer, string? audience)
    {
        if (string.IsNullOrWhiteSpace(issuer))
            return "Jwt:Issuer is required.";
        if (string.IsNullOrWhiteSpace(audience))
            return "Jwt:Audience is required.";
        return null;
    }

    /// <summary>
    /// Validates the public key a token validator is configured with. On success the imported
    /// key is returned through <paramref name="publicKey"/> (caller disposes it).
    /// </summary>
    public static string? ValidatePublicKey(string? publicKeyPem, string? publicKeyPath, out ECDsa? publicKey)
    {
        publicKey = null;

        string? pem;
        try
        {
            pem = EcdsaKeyMaterial.ResolvePem(publicKeyPem, publicKeyPath, "Jwt:PublicKeyPath");
        }
        catch (EcdsaKeyMaterialException ex)
        {
            return ex.Message;
        }

        if (pem is null)
            return $"Jwt:PublicKeyPem (or Jwt:PublicKeyPath) is required: set the CardVault {EcdsaKeyMaterial.Algorithm} public key " +
                   $"as a PEM '{EcdsaKeyMaterial.PublicKeyPemLabel}' block. See {RunbookPath}.";

        try
        {
            publicKey = EcdsaKeyMaterial.ImportPublicKey(pem);
            return null;
        }
        catch (EcdsaKeyMaterialException ex)
        {
            return $"Jwt:PublicKeyPem is invalid: {ex.Message}";
        }
    }

    /// <summary>
    /// The complete rule set for a validator-only service (IsoSwitch, IsoAudit): no retired
    /// secret, no private key, issuer and audience present, a valid P-256 public key.
    /// </summary>
    public static string? ValidateTokenValidatorOptions(
        IConfiguration configuration,
        string serviceName,
        string? issuer,
        string? audience,
        string? publicKeyPem,
        string? publicKeyPath)
    {
        var failure = RejectRetiredSymmetricSettings(configuration)
                      ?? RejectIssuerOnlySettings(configuration, serviceName)
                      ?? ValidateIssuerAndAudience(issuer, audience);
        if (failure is not null)
            return failure;

        failure = ValidatePublicKey(publicKeyPem, publicKeyPath, out var publicKey);
        publicKey?.Dispose();
        return failure;
    }

    /// <summary>Loads the configured public key for use at request time. Options are already validated.</summary>
    public static ECDsa LoadPublicKey(string? publicKeyPem, string? publicKeyPath)
    {
        var pem = EcdsaKeyMaterial.ResolvePem(publicKeyPem, publicKeyPath, "Jwt:PublicKeyPath")
                  ?? throw new EcdsaKeyMaterialException("Jwt:PublicKeyPem (or Jwt:PublicKeyPath) is required.");
        return EcdsaKeyMaterial.ImportPublicKey(pem);
    }
}
