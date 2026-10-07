using System.Security.Cryptography;
using BuildingBlocks.Security;
using IsoSwitch.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Tests.Security;

/// <summary>
/// G1-T5: IsoSwitch only validates tokens, so its JWT options carry the CardVault ES256
/// public key and nothing else. The validator fails closed when the key is missing or
/// malformed, when a private key is handed to it (in the public slot or through the
/// issuer-only settings), or when the retired HS256 <c>Jwt:SigningKey</c> is still present.
/// </summary>
public class JwtOptionsValidatorTests
{
    private static readonly EcdsaKeyPairPem Pair = EcdsaKeyMaterial.GenerateKeyPair();

    private static ValidateOptionsResult Validate(JwtOptions options, IDictionary<string, string?>? extraConfiguration = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(extraConfiguration ?? new Dictionary<string, string?>())
            .Build();
        return new JwtOptionsValidator(configuration).Validate(null, options);
    }

    private static JwtOptions Valid() => new() { Issuer = "CardVault", Audience = "CardSwitch", PublicKeyPem = Pair.PublicKeyPem };

    [Fact]
    public void PublicKey_Succeeds() => Assert.True(Validate(Valid()).Succeeded);

    [Fact]
    public void EscapedNewlinesInThePem_AreAccepted()
    {
        var options = Valid();
        options.PublicKeyPem = Pair.PublicKeyPem.Replace("\n", "\\n");

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void MissingPublicKey_Fails_AndNamesTheSetting()
    {
        var options = Valid();
        options.PublicKeyPem = string.Empty;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:PublicKeyPem", result.FailureMessage);
    }

    [Fact]
    public void PrivateKeyInThePublicKeySlot_Fails()
    {
        var options = Valid();
        options.PublicKeyPem = Pair.PrivateKeyPem;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("private key", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Jwt:PrivateKeyPem")]
    [InlineData("Jwt:PrivateKeyPath")]
    public void IssuerPrivateKeySettingsHandedToAValidator_Fails(string setting)
    {
        var result = Validate(Valid(), new Dictionary<string, string?> { [setting] = "anything" });

        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage);
    }

    [Theory]
    [InlineData("Jwt:SigningKey")]
    [InlineData("Jwt:Key")]
    public void LegacySymmetricKeyStillConfigured_Fails(string setting)
    {
        var result = Validate(Valid(), new Dictionary<string, string?> { [setting] = "TestJwtSigningKeyForIsoSwitchTests32Plus" });

        Assert.True(result.Failed);
        Assert.Contains(setting, result.FailureMessage);
    }

    [Fact]
    public void GarbagePem_Fails()
    {
        var options = Valid();
        options.PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nnot-a-key\n-----END PUBLIC KEY-----";

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void CurveOtherThanP256_Fails()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var options = Valid();
        options.PublicKeyPem = p384.ExportSubjectPublicKeyInfoPem();

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("P-256", result.FailureMessage);
    }

    [Theory]
    [InlineData("", "CardSwitch")]
    [InlineData("CardVault", "")]
    public void MissingIssuerOrAudience_Fails(string issuer, string audience)
    {
        var options = Valid();
        options.Issuer = issuer;
        options.Audience = audience;

        Assert.True(Validate(options).Failed);
    }
}
