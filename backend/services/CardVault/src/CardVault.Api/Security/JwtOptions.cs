namespace CardVault.Api.Security;

/// <summary>
/// JWT settings for CardVault, the only service that issues tokens (G1-T5). Bound from the
/// <c>Jwt</c> section, validated at startup by <see cref="JwtOptionsValidator"/>.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "CardVault";
    public string Audience { get; set; } = "CardSwitch";

    /// <summary>
    /// ES256 (P-256) private key as a PEM block (<c>-----BEGIN PRIVATE KEY-----</c>), inline.
    /// Never committed; supplied through a secret store or environment variable
    /// (<c>Jwt__PrivateKeyPem</c>). "\n" escape sequences are accepted for single-line values.
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Alternative to <see cref="PrivateKeyPem"/>: path of a file holding the PEM (e.g. a mounted secret).</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional: the public half of the configured private key. When present it must match;
    /// it exists so the same <c>Jwt__PublicKeyPem</c> value every validator receives can be
    /// handed to CardVault too and verified against the private key at startup.
    /// </summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    public string PublicKeyPath { get; set; } = string.Empty;

    /// <summary>
    /// Development only: generate a throwaway key pair for this process instead of reading one
    /// from configuration. Refused outside the Development environment and when a key is also
    /// configured, and always logged — never a silent fallback.
    /// </summary>
    public bool DevelopmentEphemeralKeys { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;
}
