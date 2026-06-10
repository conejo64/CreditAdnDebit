using System.ComponentModel.DataAnnotations;

namespace CardVault.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "CardVault";
    public string Audience { get; set; } = "CardSwitch";

    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}