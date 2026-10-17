using BuildingBlocks.Security;
using CardVault.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Cryptography;

namespace CardVault.Tests.Security;

/// <summary>
/// G1-T5: CardVault is the token issuer, so its JWT options must carry an ES256 private key
/// (PEM, never committed). The validator fails closed on every misconfiguration an operator
/// can plausibly make: no key, a public key where the private one belongs, a public key that
/// does not pair with the private key, the retired HS256 secret still present, an ephemeral
/// pair requested outside Development, or a curve other than P-256.
/// </summary>
public class JwtOptionsValidatorTests
{
    private static readonly EcdsaKeyPairPem Pair = EcdsaKeyMaterial.GenerateKeyPair();
    private static readonly EcdsaKeyPairPem OtherPair = EcdsaKeyMaterial.GenerateKeyPair();

    private static ValidateOptionsResult Validate(
        JwtOptions options,
        string environment = "Production",
        IDictionary<string, string?>? extraConfiguration = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(extraConfiguration ?? new Dictionary<string, string?>())
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        return new JwtOptionsValidator(configuration, env).Validate(null, options);
    }

    private static JwtOptions Valid() => new()
    {
        Issuer = "CardVault",
        Audience = "CardSwitch",
        PrivateKeyPem = Pair.PrivateKeyPem
    };

    [Fact]
    public void PrivateKeyOnly_Succeeds()
    {
        Assert.True(Validate(Valid()).Succeeded);
    }

    [Fact]
    public void PrivateKeyWithMatchingPublicKey_Succeeds()
    {
        var options = Valid();
        options.PublicKeyPem = Pair.PublicKeyPem;

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void EscapedNewlinesInThePem_AreAccepted()
    {
        // Docker .env files cannot carry multi-line values; the loader accepts "\n" escapes.
        var options = Valid();
        options.PrivateKeyPem = Pair.PrivateKeyPem.Replace("\n", "\\n");

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void MissingPrivateKey_Fails_AndNamesTheSetting()
    {
        var options = Valid();
        options.PrivateKeyPem = string.Empty;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:PrivateKeyPem", result.FailureMessage);
    }

    [Fact]
    public void PublicKeyInThePrivateKeySlot_Fails()
    {
        var options = Valid();
        options.PrivateKeyPem = Pair.PublicKeyPem;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:PrivateKeyPem", result.FailureMessage);
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

    [Fact]
    public void PublicKeyThatDoesNotPairWithThePrivateKey_Fails()
    {
        var options = Valid();
        options.PublicKeyPem = OtherPair.PublicKeyPem;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("does not match", result.FailureMessage);
    }

    [Fact]
    public void GarbagePem_Fails()
    {
        var options = Valid();
        options.PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nnot-a-key\n-----END PRIVATE KEY-----";

        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void CurveOtherThanP256_Fails()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var options = Valid();
        options.PrivateKeyPem = p384.ExportPkcs8PrivateKeyPem();

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("P-256", result.FailureMessage);
    }

    [Fact]
    public void LegacySymmetricSigningKeyStillConfigured_Fails()
    {
        var result = Validate(Valid(), extraConfiguration: new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "xK9!mP2@nQ5#rT8$vW1&yZ4*bD7^gH0_"
        });

        Assert.True(result.Failed);
        Assert.Contains("Jwt:SigningKey", result.FailureMessage);
    }

    [Fact]
    public void EphemeralKeys_InDevelopment_WithoutAConfiguredKey_Succeeds()
    {
        var options = new JwtOptions { Issuer = "CardVault", Audience = "CardSwitch", DevelopmentEphemeralKeys = true };

        Assert.True(Validate(options, environment: "Development").Succeeded);
    }

    [Fact]
    public void EphemeralKeys_OutsideDevelopment_Fails()
    {
        var options = new JwtOptions { Issuer = "CardVault", Audience = "CardSwitch", DevelopmentEphemeralKeys = true };

        var result = Validate(options, environment: "Production");

        Assert.True(result.Failed);
        Assert.Contains("DevelopmentEphemeralKeys", result.FailureMessage);
    }

    [Fact]
    public void EphemeralKeys_TogetherWithAConfiguredKey_Fails()
    {
        // Either the operator supplied a key or asked for a throwaway one; both at once is a
        // configuration mistake that must not be resolved silently.
        var options = Valid();
        options.DevelopmentEphemeralKeys = true;

        Assert.True(Validate(options, environment: "Development").Failed);
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

    [Fact]
    public void NonPositiveAccessTokenLifetime_Fails()
    {
        var options = Valid();
        options.AccessTokenMinutes = 0;

        Assert.True(Validate(options).Failed);
    }
}
