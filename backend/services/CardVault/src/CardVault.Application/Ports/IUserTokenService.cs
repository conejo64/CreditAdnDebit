using CardVault.Infrastructure.Identity.Auth;
using CardVault.Application.Contracts;

namespace CardVault.Application.Ports;

/// <summary>
/// Port for issuing and managing user JWT access tokens.
/// Implemented by CardVault.Api.Security.TokenService (stays in Api).
/// </summary>
public interface IUserTokenService
{
    Task<string> CreateAccessTokenAsync(AppUser user);
    Task<AuthenticatedUserResponse> BuildAuthenticatedUserAsync(AppUser user);
    string CreateOpenBankingAccessToken(string clientId, string clientName, IEnumerable<string> scopes);
    int GetAccessTokenLifetimeSeconds();

    // Static helpers exposed as instance for testability
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}
