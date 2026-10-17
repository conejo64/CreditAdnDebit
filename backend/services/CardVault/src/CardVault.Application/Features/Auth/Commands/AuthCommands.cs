using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CardVault.Infrastructure.Identity.Auth;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;
using CardVault.Application.Services;

namespace CardVault.Application.Features.Auth.Commands;

public record RegisterUserCommand(RegisterRequest Request) : IRequest<IResult>;
public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, IResult>
{
    private readonly UserManager<AppUser> _users;

    public RegisterUserCommandHandler(UserManager<AppUser> users)
    {
        _users = users;
    }

    public async Task<IResult> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var exists = await _users.FindByEmailAsync(req.Email);
        if (exists is not null)
            return Results.Conflict(new { message = "User already exists" });

        var user = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            EmailConfirmed = true
        };

        var res = await _users.CreateAsync(user, req.Password);
        if (!res.Succeeded)
            return Results.BadRequest(res.Errors);

        return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Email });
    }
}

/// <summary>
/// G1-T1 (PCI DSS v4 8.3.4): every password check on an anonymous endpoint goes through
/// Identity lockout. The three outcomes an attacker could probe (unknown user, wrong
/// password, locked account) share one response so the account state is never disclosed.
/// Lockout is driven through <see cref="UserManager{TUser}"/> directly (IsLockedOutAsync /
/// AccessFailedAsync / ResetAccessFailedCountAsync) rather than SignInManager, so the
/// handlers stay free of HttpContext and remain unit-testable with a substituted manager.
/// </summary>
internal static class PasswordCheckGuard
{
    private static string? _dummyHash;

    public static IResult GenericUnauthorized()
        => Results.Json(new { message = "Unauthorized" }, statusCode: 401);

    /// <summary>
    /// Returns true only when the user exists, is not locked out and the password matches.
    /// A wrong password increments the Identity failure counter (locking the account when
    /// the configured threshold is reached); a correct one resets it.
    /// </summary>
    public static async Task<bool> VerifyAsync(UserManager<AppUser> users, AppUser? user, string password)
    {
        if (user is null)
        {
            // Spend roughly the same time as a real hash verification so an unknown
            // e-mail is not cheaper to probe than a wrong password.
            EqualizeTiming(users, password);
            return false;
        }

        if (await users.IsLockedOutAsync(user))
            return false;

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return false;
        }

        await users.ResetAccessFailedCountAsync(user);
        return true;
    }

    private static void EqualizeTiming(UserManager<AppUser> users, string password)
    {
        var hasher = users.PasswordHasher;
        if (hasher is null)
            return;

        var probe = new AppUser();
        _dummyHash ??= hasher.HashPassword(probe, Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(probe, _dummyHash, password ?? string.Empty);
    }
}

public record LoginCommand(LoginRequest Request) : IRequest<IResult>;
public class LoginCommandHandler : IRequestHandler<LoginCommand, IResult>
{
    private readonly UserManager<AppUser> _users;
    private readonly IUserTokenService _tokens;
    private readonly IdentityAppDbContext _idDb;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public LoginCommandHandler(UserManager<AppUser> users, IUserTokenService tokens, IdentityAppDbContext idDb, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _users = users;
        _tokens = tokens;
        _idDb = idDb;
        _config = config;
    }

    public async Task<IResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var user = await _users.FindByEmailAsync(req.Email);
        if (!await PasswordCheckGuard.VerifyAsync(_users, user, req.Password))
            return PasswordCheckGuard.GenericUnauthorized();

        // If user has MFA enabled, require it
        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return Results.Ok(new AuthSessionResponse(true, null, null, "MFA_REQUIRED", null));
        }

        var access = await _tokens.CreateAccessTokenAsync(user);
        var refresh = _tokens.GenerateRefreshToken();
        var hash = _tokens.HashRefreshToken(refresh);

        int refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var rd) ? rd : 7;
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(refreshDays)
        });
        await _idDb.SaveChangesAsync(cancellationToken);

        var authenticatedUser = await _tokens.BuildAuthenticatedUserAsync(user);
        return Results.Ok(new AuthSessionResponse(false, access, refresh, "OK", authenticatedUser));
    }
}

public record MfaEnableCommand(MfaEnableRequest Request) : IRequest<IResult>;
public class MfaEnableCommandHandler : IRequestHandler<MfaEnableCommand, IResult>
{
    private readonly UserManager<AppUser> _users;

    public MfaEnableCommandHandler(UserManager<AppUser> users)
    {
        _users = users;
    }

    public async Task<IResult> Handle(MfaEnableCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var user = await _users.FindByEmailAsync(req.Email);
        if (!await PasswordCheckGuard.VerifyAsync(_users, user, req.Password))
            return PasswordCheckGuard.GenericUnauthorized();

        // Reset key and enable 2FA
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);
        var key = await _users.GetAuthenticatorKeyAsync(user) ?? "";
        var uri = $"otpauth://totp/CardVault:{Uri.EscapeDataString(user.Email ?? "")}?secret={key}&issuer=CardVault&digits=6";
        var recoveryCodes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        return Results.Ok(new MfaEnableResponse(uri, key, recoveryCodes?.ToArray() ?? Array.Empty<string>()));
    }
}

public record MfaVerifyCommand(MfaVerifyRequest Request) : IRequest<IResult>;
public class MfaVerifyCommandHandler : IRequestHandler<MfaVerifyCommand, IResult>
{
    private readonly UserManager<AppUser> _users;
    private readonly IUserTokenService _tokens;
    private readonly IdentityAppDbContext _idDb;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public MfaVerifyCommandHandler(UserManager<AppUser> users, IUserTokenService tokens, IdentityAppDbContext idDb, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _users = users;
        _tokens = tokens;
        _idDb = idDb;
        _config = config;
    }

    public async Task<IResult> Handle(MfaVerifyCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var user = await _users.FindByEmailAsync(req.Email);
        if (!await PasswordCheckGuard.VerifyAsync(_users, user, req.Password))
            return PasswordCheckGuard.GenericUnauthorized();

        var valid = await _users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, req.Code);
        if (!valid)
            return Results.Json(new { message = "Invalid MFA code" }, statusCode: 401);

        var access = await _tokens.CreateAccessTokenAsync(user);
        var refresh = _tokens.GenerateRefreshToken();
        var hash = _tokens.HashRefreshToken(refresh);

        int refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var rd) ? rd : 7;
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(refreshDays)
        });
        await _idDb.SaveChangesAsync(cancellationToken);

        var authenticatedUser = await _tokens.BuildAuthenticatedUserAsync(user);
        return Results.Ok(new AuthSessionResponse(false, access, refresh, "OK", authenticatedUser));
    }
}

public record RefreshTokenCommand(RefreshRequest Request) : IRequest<IResult>;
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, IResult>
{
    private readonly IdentityAppDbContext _idDb;
    private readonly UserManager<AppUser> _users;
    private readonly IUserTokenService _tokens;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public RefreshTokenCommandHandler(IdentityAppDbContext idDb, UserManager<AppUser> users, IUserTokenService tokens, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _idDb = idDb;
        _users = users;
        _tokens = tokens;
        _config = config;
    }

    public async Task<IResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var refreshToken = req.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        var hash = _tokens.HashRefreshToken(refreshToken);
        var stored = await _idDb.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (stored is null || !stored.IsActive)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        var user = await _users.FindByIdAsync(stored.UserId);
        if (user is null)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        // rotate
        stored.RevokedOn = DateTimeOffset.UtcNow;
        var newRefresh = _tokens.GenerateRefreshToken();
        var newHash = _tokens.HashRefreshToken(newRefresh);
        stored.ReplacedByTokenHash = newHash;

        int refreshDays = int.TryParse(_config["Jwt:RefreshTokenDays"], out var rd) ? rd : 7;
        _idDb.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(refreshDays)
        });

        var access = await _tokens.CreateAccessTokenAsync(user);
        await _idDb.SaveChangesAsync(cancellationToken);

        var authenticatedUser = await _tokens.BuildAuthenticatedUserAsync(user);
        return Results.Ok(new AuthSessionResponse(false, access, newRefresh, "OK", authenticatedUser));
    }
}

// SEC-03: logout revokes the stored refresh token (if any is supplied) so a captured
// cv_rt value cannot be used to mint new sessions after logout. Clearing the cookies
// themselves is a presentation-layer concern (AuthController), not this handler's job —
// this handler stays pure, DB-only, no HttpResponse dependency.
public record LogoutCommand(string? RefreshToken) : IRequest<IResult>;
public class LogoutCommandHandler : IRequestHandler<LogoutCommand, IResult>
{
    private readonly IdentityAppDbContext _idDb;
    private readonly IUserTokenService _tokens;

    public LogoutCommandHandler(IdentityAppDbContext idDb, IUserTokenService tokens)
    {
        _idDb = idDb;
        _tokens = tokens;
    }

    public async Task<IResult> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            var hash = _tokens.HashRefreshToken(request.RefreshToken);
            var stored = await _idDb.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
            if (stored is not null && stored.RevokedOn is null)
            {
                stored.RevokedOn = DateTimeOffset.UtcNow;
                await _idDb.SaveChangesAsync(cancellationToken);
            }
        }

        return Results.NoContent();
    }
}

public record GetCurrentUserQuery(string UserId) : IRequest<IResult>;
public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, IResult>
{
    private readonly UserManager<AppUser> _users;
    private readonly IUserTokenService _tokens;

    public GetCurrentUserQueryHandler(UserManager<AppUser> users, IUserTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<IResult> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        var user = await _users.FindByIdAsync(request.UserId);
        if (user is null)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        var authenticatedUser = await _tokens.BuildAuthenticatedUserAsync(user);
        return Results.Ok(authenticatedUser);
    }
}

// ── Password Recovery: ForgotPassword ────────────────────────────────────────

public record ForgotPasswordCommand(ForgotPasswordRequest Request) : IRequest<IResult>;
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, IResult>
{
    private readonly IPasswordResetService _pwdReset;

    public ForgotPasswordCommandHandler(IPasswordResetService pwdReset)
        => _pwdReset = pwdReset;

    public async Task<IResult> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        // Always return 202 — never reveal whether the email exists (security, spec HC-2-S4)
        await _pwdReset.CreateTokenAsync(request.Request.Email, cancellationToken);
        return Results.Accepted();
    }
}

// ── Password Recovery: ResetPassword ─────────────────────────────────────────

public record ResetPasswordCommand(ResetPasswordByTokenRequest Request) : IRequest<IResult>;
public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, IResult>
{
    private readonly IPasswordResetService _pwdReset;

    public ResetPasswordCommandHandler(IPasswordResetService pwdReset)
        => _pwdReset = pwdReset;

    public async Task<IResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var ok = await _pwdReset.ResetPasswordAsync(
            request.Request.Token, request.Request.NewPassword, cancellationToken);

        return ok
            ? Results.NoContent()
            : Results.BadRequest(new { message = "Invalid or expired token." });
    }
}
