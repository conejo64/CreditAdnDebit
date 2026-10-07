using CardVault.Api.Security;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Infrastructure.Identity.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CardVault.Api.Security;

public class TokenService : IUserTokenService, IOpenBankingTokenIssuer
{
    /// <summary>
    /// G1-T3: the pending token is a JWT, but it is deliberately NOT a session token:
    /// a distinct audience (<see cref="MfaPendingAudienceSuffix"/> appended to the session
    /// audience) makes the JwtBearer handler reject it, a distinct <c>typ</c> header and a
    /// <c>purpose</c> claim keep it from being confused with anything else, and it lives at
    /// most <see cref="MfaPendingTokenLifetime"/>. Single use is enforced by the handler
    /// through the recorded <c>jti</c>, not by the token itself.
    /// </summary>
    public const string MfaPendingTokenType = "mfa-pending";
    public const string MfaPendingPurposeClaim = "purpose";
    public const string MfaPendingAudienceSuffix = ":mfa-pending";
    public static readonly TimeSpan MfaPendingTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly JwtOptions _opt;
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<IdentityRole> _roleManager;

    public TokenService(IOptions<JwtOptions> opt, UserManager<AppUser> users, RoleManager<IdentityRole> roleManager)
    {
        _opt = opt.Value;
        _users = users;
        _roleManager = roleManager;
    }

    public virtual async Task<string> CreateAccessTokenAsync(AppUser user)
    {
        var roles = await _users.GetRolesAsync(user);
        var userClaims = await _users.GetClaimsAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new("uid", user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        // Permisos directos del usuario
        foreach (var c in userClaims.Where(c => c.Type == "perm"))
            claims.Add(c);

        // Permisos heredados de los roles (RoleClaims)
        var addedPerms = new HashSet<string>(userClaims.Where(c => c.Type == "perm").Select(c => c.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var roleClaims = await _roleManager.GetClaimsAsync(role);
            foreach (var rc in roleClaims.Where(c => c.Type == "perm"))
            {
                if (addedPerms.Add(rc.Value))
                    claims.Add(rc);
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateOpenBankingAccessToken(string clientId, string clientName, IEnumerable<string> scopes)
    {
        var scopeList = scopes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new("client_id", clientId),
            new("client_name", clientName),
            new("grant_type", "client_credentials"),
            new(ClaimTypes.Name, clientName),
            new("scope", string.Join(' ', scopeList))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int GetAccessTokenLifetimeSeconds() => _opt.AccessTokenMinutes * 60;

    public MfaPendingTokenIssue CreateMfaPendingToken(AppUser user)
    {
        var jti = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(MfaPendingTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("uid", user.Id),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(MfaPendingPurposeClaim, MfaPendingTokenType)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience + MfaPendingAudienceSuffix,
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);
        token.Header["typ"] = MfaPendingTokenType;

        return new MfaPendingTokenIssue(new JwtSecurityTokenHandler().WriteToken(token), jti, expires);
    }

    public MfaPendingTokenClaims? ValidateMfaPendingToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _opt.Issuer,
            ValidateAudience = true,
            ValidAudience = _opt.Audience + MfaPendingAudienceSuffix,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = [MfaPendingTokenType],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            handler.ValidateToken(token, parameters, out var validated);
            if (validated is not JwtSecurityToken jwt)
                return null;

            var purpose = jwt.Claims.FirstOrDefault(c => c.Type == MfaPendingPurposeClaim)?.Value;
            var userId = jwt.Claims.FirstOrDefault(c => c.Type == "uid")?.Value ?? jwt.Subject;
            if (purpose != MfaPendingTokenType || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(jwt.Id))
                return null;

            return new MfaPendingTokenClaims(userId, jwt.Id);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed input that is not even a JWT.
            return null;
        }
    }

    public async Task<AuthenticatedUserResponse> BuildAuthenticatedUserAsync(AppUser user)
    {
        var roles = (await _users.GetRolesAsync(user))
            .OrderBy(GetRolePriority)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Permisos directos del usuario
        var userPerms = (await _users.GetClaimsAsync(user))
            .Where(c => c.Type == "perm")
            .Select(c => c.Value);

        // Permisos heredados de los roles
        var allPerms = new HashSet<string>(userPerms, StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;
            var roleClaims = await _roleManager.GetClaimsAsync(role);
            foreach (var rc in roleClaims.Where(c => c.Type == "perm"))
                allPerms.Add(rc.Value);
        }

        var permissions = allPerms
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var email = user.Email ?? string.Empty;
        var name = string.IsNullOrWhiteSpace(user.UserName)
            ? (string.IsNullOrWhiteSpace(email) ? user.Id : email.Split('@')[0])
            : user.UserName;

        return new AuthenticatedUserResponse(
            user.Id,
            email,
            name,
            roles.FirstOrDefault() ?? "User",
            roles,
            permissions);
    }

    private static int GetRolePriority(string role) => role switch
    {
        "Admin" => 0,
        "Operator" => 1,
        "Auditor" => 2,
        _ => 99
    };

    public static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public static string HashRefreshToken(string refreshToken)
    {
        // SHA-256 is OK for hashing tokens (token already random). In production you can use HMAC with server secret.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }

    // IUserTokenService explicit instance implementations for static helpers
    string IUserTokenService.GenerateRefreshToken() => GenerateRefreshToken();
    string IUserTokenService.HashRefreshToken(string refreshToken) => HashRefreshToken(refreshToken);
}
