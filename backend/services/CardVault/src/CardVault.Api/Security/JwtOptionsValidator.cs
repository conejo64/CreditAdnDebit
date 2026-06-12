using Microsoft.Extensions.Options;

namespace CardVault.Api.Security;

/// <summary>
/// Custom IValidateOptions that rejects known DEV placeholder literals and
/// signing keys shorter than 32 characters (ADR-1, SEC-2).
/// DataAnnotations alone cannot catch a 32-char placeholder that satisfies MinLength.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private static readonly string[] Forbidden =
        ["DEV_ONLY", "CHANGE_ME", "change_me", "placeholder"];

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey must be at least 32 characters.");

        if (Forbidden.Any(f => options.SigningKey.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey is a known placeholder value; set a real secret.");

        return ValidateOptionsResult.Success;
    }
}
