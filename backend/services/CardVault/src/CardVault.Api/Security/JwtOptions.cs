namespace CardVault.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "CardVault";
    public string Audience { get; set; } = "CardSwitch";
    public string SigningKey { get; set; } = "DEV_ONLY_change_me_please_32+chars";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}