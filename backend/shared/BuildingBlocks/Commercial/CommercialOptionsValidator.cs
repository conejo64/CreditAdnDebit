using Microsoft.Extensions.Options;

namespace BuildingBlocks.Commercial;

public sealed class CommercialOptionsValidator : IValidateOptions<CommercialOptions>
{
    public ValidateOptionsResult Validate(string? name, CommercialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClaimRegisterVersion))
        {
            failures.Add("Commercial:ClaimRegisterVersion must be provided.");
        }

        if (options.Mode == CommercialMode.Commercial)
        {
            if (options.EnableDemoSurfaces)
            {
                failures.Add("Commercial mode must not enable Commercial:EnableDemoSurfaces.");
            }

            if (options.EnableAnonymousDiagnostics)
            {
                failures.Add("Commercial mode must not enable Commercial:EnableAnonymousDiagnostics.");
            }

            if (options.EnableSwagger)
            {
                failures.Add("Commercial mode must not enable Commercial:EnableSwagger.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
