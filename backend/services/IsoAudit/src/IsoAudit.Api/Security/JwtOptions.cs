using System.ComponentModel.DataAnnotations;

namespace IsoAudit.Api.Security;

/// <summary>
/// Strongly-typed options for the IsoAudit JWT key (ADR-1).
/// Bound from the "Jwt" configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}
