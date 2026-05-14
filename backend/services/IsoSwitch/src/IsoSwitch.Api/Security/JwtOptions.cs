namespace IsoSwitch.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "CardVault";
    public string Audience { get; init; } = "CardSwitch";
    public string SigningKey { get; init; } = string.Empty;
}
