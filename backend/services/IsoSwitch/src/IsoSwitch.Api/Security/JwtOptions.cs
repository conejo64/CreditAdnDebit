namespace IsoSwitch.Api.Security;

/// <summary>
/// JWT settings for IsoSwitch, a token validator (G1-T5). It holds the CardVault ES256 public
/// key and nothing that could sign a token. Bound from the <c>Jwt</c> section, validated at
/// startup by <see cref="JwtOptionsValidator"/>.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "CardVault";
    public string Audience { get; set; } = "CardSwitch";

    /// <summary>
    /// CardVault's ES256 public key as a PEM block (<c>-----BEGIN PUBLIC KEY-----</c>), inline
    /// (<c>Jwt__PublicKeyPem</c>). "\n" escape sequences are accepted for single-line values.
    /// </summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>Alternative to <see cref="PublicKeyPem"/>: path of a file holding the PEM.</summary>
    public string PublicKeyPath { get; set; } = string.Empty;
}
