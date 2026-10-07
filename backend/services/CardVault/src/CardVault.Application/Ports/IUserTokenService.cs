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

    /// <summary>
    /// G1-T3: mints the short-lived MFA pending token for <paramref name="user"/>. The caller
    /// records <see cref="MfaPendingTokenIssue.Jti"/> server-side to make the token single-use.
    /// </summary>
    MfaPendingTokenIssue CreateMfaPendingToken(AppUser user);

    /// <summary>
    /// G1-T3: validates signature, issuer, the dedicated MFA audience, type, purpose and
    /// expiry of a pending token. Returns null for anything that is not a live pending token
    /// (including a regular session token).
    /// </summary>
    MfaPendingTokenClaims? ValidateMfaPendingToken(string token);
}

/// <summary>A freshly minted MFA pending token and the identifier recorded for single use.</summary>
public sealed record MfaPendingTokenIssue(string Token, string Jti, DateTimeOffset ExpiresOn);

/// <summary>The claims the MFA verify step needs from a validated pending token.</summary>
public sealed record MfaPendingTokenClaims(string UserId, string Jti);
