using Microsoft.Extensions.Options;

namespace IsoSwitch.Api.Security;

/// <summary>
/// Custom IValidateOptions that rejects known DEV placeholder literals
/// (including the historical "dev-admin-key" default) and admin API keys
/// shorter than 32 characters (SEC-05/SEC-11). Mirrors JwtOptionsValidator /
/// TokenizationOptionsValidator exactly — DataAnnotations alone cannot catch a
/// 32-char placeholder that satisfies MinLength.
/// </summary>
public sealed class AdminApiKeyOptionsValidator : IValidateOptions<AdminApiKeyOptions>
{
    private static readonly string[] Forbidden =
        ["DEV_ONLY", "CHANGE_ME", "change_me", "placeholder", "dev-admin-key"];

    public ValidateOptionsResult Validate(string? name, AdminApiKeyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length < 32)
            return ValidateOptionsResult.Fail(
                "Admin:ApiKey must be at least 32 characters.");

        if (Forbidden.Any(f => options.ApiKey.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail(
                "Admin:ApiKey is a known placeholder value; set a real operator-supplied secret.");

        return ValidateOptionsResult.Success;
    }
}
