using System.Security.Cryptography;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CardVault.Api.Security;

/// <summary>
/// G1-T5: the process-wide ES256 key pair CardVault signs with. Built once from the validated
/// <see cref="JwtOptions"/>: the private key becomes <see cref="SigningCredentials"/>, the
/// public half becomes the bearer validation key and the JWKS entry, and the <c>kid</c> is the
/// RFC 7638 thumbprint of the public key so a rotation is visible as a key-id change only.
/// </summary>
/// <remarks>
/// Deliberately not IDisposable. Microsoft.IdentityModel caches signature providers in a
/// process-wide cache keyed by the key's JWK thumbprint; disposing the ECDsa instances when a
/// host shuts down would leave a cached provider over a disposed key for any later host in the
/// same process that uses the same key (every test factory does). The pair lives as long as
/// the process, which is also its lifetime in production.
/// </remarks>
public sealed class JwtSigningKeys
{
    private readonly ECDsa _privateKey;
    private readonly ECDsa _publicKey;

    public JwtSigningKeys(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKeys> logger)
    {
        var jwt = options.Value;
        var privatePem = EcdsaKeyMaterial.ResolvePem(jwt.PrivateKeyPem, jwt.PrivateKeyPath, "Jwt:PrivateKeyPath");

        if (privatePem is null)
        {
            if (!(jwt.DevelopmentEphemeralKeys && environment.IsDevelopment()))
                throw new InvalidOperationException(
                    $"CardVault has no JWT signing key: configure Jwt:PrivateKeyPem. See {JwtKeyConfigurationRules.RunbookPath}.");

            privatePem = EcdsaKeyMaterial.GenerateKeyPair().PrivateKeyPem;
            IsEphemeral = true;
            logger.LogWarning(
                "Jwt:DevelopmentEphemeralKeys is enabled: CardVault generated a throwaway {Algorithm} key pair for this process. " +
                "Tokens will not survive a restart and no other service can validate them.",
                EcdsaKeyMaterial.Algorithm);
        }

        _privateKey = EcdsaKeyMaterial.ImportPrivateKey(privatePem);
        // A separate public-only instance so verification and the JWKS never touch the private key object.
        _publicKey = EcdsaKeyMaterial.ImportPublicKey(EcdsaKeyMaterial.ExportPublicKeyPem(_privateKey));

        KeyId = EcdsaKeyMaterial.ComputeKeyId(_publicKey);
        SigningCredentials = new SigningCredentials(
            new ECDsaSecurityKey(_privateKey) { KeyId = KeyId },
            SecurityAlgorithms.EcdsaSha256);
        PublicSecurityKey = new ECDsaSecurityKey(_publicKey) { KeyId = KeyId };
        PublicJwk = EcdsaKeyMaterial.ToPublicJwk(_publicKey);
    }

    /// <summary>RFC 7638 thumbprint of the public key; written as the <c>kid</c> header of every token.</summary>
    public string KeyId { get; }

    /// <summary>True when the pair was generated for this process (Development only, explicit opt-in).</summary>
    public bool IsEphemeral { get; }

    public SigningCredentials SigningCredentials { get; }

    public ECDsaSecurityKey PublicSecurityKey { get; }

    public EcdsaPublicJwk PublicJwk { get; }
}
