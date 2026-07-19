namespace IsoSwitch.Api.Security;

/// <summary>
/// Strongly-typed options for the IsoSwitch admin API key (SEC-05/SEC-11).
/// Bound from the "Admin" configuration section. There is no consumer of this
/// key in request-time middleware yet — see AdminApiKeyOptionsValidator for
/// the fail-fast validation contract this options type establishes.
/// </summary>
public sealed class AdminApiKeyOptions
{
    public const string Section = "Admin";

    public string ApiKey { get; set; } = string.Empty;
}
