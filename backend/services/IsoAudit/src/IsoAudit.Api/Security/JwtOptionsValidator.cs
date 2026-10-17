using BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace IsoAudit.Api.Security;

/// <summary>
/// G1-T5: fail-closed startup validation of IsoAudit's JWT settings. The service must hold a
/// valid ES256 public key, must not be handed a private key, and must no longer carry the
/// retired HS256 <c>Jwt:Key</c>.
/// </summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private readonly IConfiguration _configuration;

    public JwtOptionsValidator(IConfiguration configuration) => _configuration = configuration;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failure = JwtKeyConfigurationRules.ValidateTokenValidatorOptions(
            _configuration, "IsoAudit", options.Issuer, options.Audience, options.PublicKeyPem, options.PublicKeyPath);

        return failure is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failure);
    }
}
