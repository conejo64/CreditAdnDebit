using System.ComponentModel.DataAnnotations;

namespace IsoSwitch.Api.Security;

/// <summary>
/// Strongly-typed options for the tokenization secret (ADR-1).
/// Bound from the "Tokenization" configuration section.
/// </summary>
public sealed class TokenizationOptions
{
    public const string Section = "Tokenization";

    [Required]
    [MinLength(32)]
    public string Secret { get; set; } = string.Empty;
}
