namespace CardVault.Application.Ports;

/// <summary>
/// Port for issuing Open Banking JWT access tokens.
/// Implemented by CardVault.Api.Security.TokenService (stays in Api).
/// </summary>
public interface IOpenBankingTokenIssuer
{
    string CreateOpenBankingAccessToken(string clientId, string clientName, IEnumerable<string> scopes);
    int GetAccessTokenLifetimeSeconds();
}
