using Microsoft.Extensions.Options;

namespace CardVault.Api.Security;

/// <summary>
/// Custom IValidateOptions that fails startup fast when a required connection
/// string is absent from all configuration sources (SEC-9). Mirrors the
/// JwtOptionsValidator / TokenizationOptionsValidator fail-fast pattern already
/// established in this codebase, and references the missing configuration key
/// in the error message so an operator knows exactly what to supply.
/// </summary>
public sealed class RequiredConnectionStringsOptionsValidator : IValidateOptions<RequiredConnectionStringsOptions>
{
    public ValidateOptionsResult Validate(string? name, RequiredConnectionStringsOptions options)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Postgres))
            missing.Add("ConnectionStrings:Postgres");

        if (string.IsNullOrWhiteSpace(options.SqlServerIdentity))
            missing.Add("ConnectionStrings:SqlServerIdentity");

        if (missing.Count > 0)
            return ValidateOptionsResult.Fail(
                $"Missing required configuration value(s): {string.Join(", ", missing)}. " +
                "Set these via environment variables or a secrets-manager provider before starting CardVault.Api.");

        return ValidateOptionsResult.Success;
    }
}
