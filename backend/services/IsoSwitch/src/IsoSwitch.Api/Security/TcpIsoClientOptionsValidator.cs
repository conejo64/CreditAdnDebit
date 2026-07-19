using System.Net;
using IsoSwitch.Infrastructure.SwitchIso8583.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IsoSwitch.Api.Security;

/// <summary>
/// Custom IValidateOptions that fails startup fast when the ISO 8583 TCP
/// channel would run in a non-Development environment with TLS disabled
/// against a non-loopback acquirer host (SEC-04/SEC-10). Plaintext stays
/// permitted only for loopback hosts, and only outside Development. Mirrors
/// the TokenizationOptionsValidator / JwtOptionsValidator fail-fast pattern
/// already established in this codebase.
/// </summary>
public sealed class TcpIsoClientOptionsValidator : IValidateOptions<TcpIsoClientOptions>
{
    private readonly IHostEnvironment _env;

    public TcpIsoClientOptionsValidator(IHostEnvironment env)
    {
        _env = env;
    }

    public ValidateOptionsResult Validate(string? name, TcpIsoClientOptions options)
    {
        if (!_env.IsDevelopment() && !options.UseTls && !IsLoopback(options.Host))
        {
            return ValidateOptionsResult.Fail(
                $"IsoClient:UseTls is false for non-loopback acquirer host '{options.Host}'. " +
                "TLS is required outside Development unless the configured host is a loopback " +
                "address (localhost, 127.0.0.1, or ::1).");
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Fast path: literal IP addresses via IPAddress.TryParse/IsLoopback.
    /// Falls back to DNS resolution for hostnames, treating "localhost" as
    /// loopback explicitly. Fails closed: if resolution fails or produces no
    /// addresses, the host is treated as NON-loopback so TLS stays required.
    /// </summary>
    internal static bool IsLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var literalAddress))
            return IPAddress.IsLoopback(literalAddress);

        try
        {
            var resolved = Dns.GetHostAddresses(host);
            return resolved.Length > 0 && resolved.All(IPAddress.IsLoopback);
        }
        catch
        {
            // Fail closed: an unresolvable hostname is treated as non-loopback,
            // so TLS stays required rather than silently permitting plaintext.
            return false;
        }
    }
}
