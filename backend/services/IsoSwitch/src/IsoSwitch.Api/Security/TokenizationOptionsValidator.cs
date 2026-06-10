using Microsoft.Extensions.Options;

namespace IsoSwitch.Api.Security;

/// <summary>
/// Custom IValidateOptions that rejects known DEV placeholder literals and
/// secrets shorter than 32 characters (ADR-1, SEC-1).
/// DataAnnotations alone cannot catch a 32-char placeholder that satisfies MinLength.
/// </summary>
public sealed class TokenizationOptionsValidator : IValidateOptions<TokenizationOptions>
{
    private static readonly string[] Forbidden =
        ["DEV_ONLY", "CHANGE_ME", "change_me", "placeholder"];

    public ValidateOptionsResult Validate(string? name, TokenizationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret) || options.Secret.Length < 32)
            return ValidateOptionsResult.Fail(
                "Tokenization:Secret must be at least 32 characters.");

        if (Forbidden.Any(f => options.Secret.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail(
                "Tokenization:Secret is a known placeholder value; set a real secret.");

        return ValidateOptionsResult.Success;
    }
}
