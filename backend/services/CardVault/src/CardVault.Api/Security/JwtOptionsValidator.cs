using System.Security.Cryptography;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace CardVault.Api.Security;

/// <summary>
/// G1-T5: fail-closed startup validation of the issuer's JWT settings. CardVault must hold a
/// valid ES256 private key (or, in Development only and on explicit request, generate one);
/// a configured public key must pair with it; the retired HS256 <c>Jwt:SigningKey</c> must be
/// gone. Every message names the setting an operator has to fix.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public JwtOptionsValidator(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failure = JwtKeyConfigurationRules.RejectRetiredSymmetricSettings(_configuration)
                      ?? JwtKeyConfigurationRules.ValidateIssuerAndAudience(options.Issuer, options.Audience);
        if (failure is not null)
            return ValidateOptionsResult.Fail(failure);

        if (options.AccessTokenMinutes <= 0)
            return ValidateOptionsResult.Fail("Jwt:AccessTokenMinutes must be greater than zero.");

        string? privatePem;
        string? publicPem;
        try
        {
            privatePem = EcdsaKeyMaterial.ResolvePem(options.PrivateKeyPem, options.PrivateKeyPath, "Jwt:PrivateKeyPath");
            publicPem = EcdsaKeyMaterial.ResolvePem(options.PublicKeyPem, options.PublicKeyPath, "Jwt:PublicKeyPath");
        }
        catch (EcdsaKeyMaterialException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        if (options.DevelopmentEphemeralKeys)
        {
            if (!_environment.IsDevelopment())
                return ValidateOptionsResult.Fail(
                    "Jwt:DevelopmentEphemeralKeys is only honoured in the Development environment; " +
                    $"configure Jwt:PrivateKeyPem instead. See {JwtKeyConfigurationRules.RunbookPath}.");

            if (privatePem is not null || publicPem is not null)
                return ValidateOptionsResult.Fail(
                    "Jwt:DevelopmentEphemeralKeys cannot be combined with Jwt:PrivateKeyPem or Jwt:PublicKeyPem: " +
                    "either supply a key pair or ask for a throwaway one, not both.");

            return ValidateOptionsResult.Success;
        }

        if (privatePem is null)
            return ValidateOptionsResult.Fail(
                $"Jwt:PrivateKeyPem (or Jwt:PrivateKeyPath) is required: CardVault signs tokens with an {EcdsaKeyMaterial.Algorithm} " +
                "private key (PEM '-----BEGIN PRIVATE KEY-----'). In Development, Jwt:DevelopmentEphemeralKeys=true generates a " +
                $"throwaway pair. See {JwtKeyConfigurationRules.RunbookPath}.");

        ECDsa privateKey;
        try
        {
            privateKey = EcdsaKeyMaterial.ImportPrivateKey(privatePem);
        }
        catch (EcdsaKeyMaterialException ex)
        {
            return ValidateOptionsResult.Fail($"Jwt:PrivateKeyPem is invalid: {ex.Message}");
        }

        using (privateKey)
        {
            if (publicPem is null)
                return ValidateOptionsResult.Success;

            ECDsa publicKey;
            try
            {
                publicKey = EcdsaKeyMaterial.ImportPublicKey(publicPem);
            }
            catch (EcdsaKeyMaterialException ex)
            {
                return ValidateOptionsResult.Fail($"Jwt:PublicKeyPem is invalid: {ex.Message}");
            }

            using (publicKey)
            {
                if (!EcdsaKeyMaterial.PublicKeyMatches(privateKey, publicKey))
                    return ValidateOptionsResult.Fail(
                        "Jwt:PublicKeyPem does not match Jwt:PrivateKeyPem: the public key must be derived from the configured private key.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
