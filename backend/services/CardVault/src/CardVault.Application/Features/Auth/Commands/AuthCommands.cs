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
        if (user is null)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        if (!await _users.CheckPasswordAsync(user, req.Password))
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

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
        if (user is null)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        if (!await _users.CheckPasswordAsync(user, req.Password))
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

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
        if (user is null)
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

        if (!await _users.CheckPasswordAsync(user, req.Password))
            return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

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
        var hash = _tokens.HashRefreshToken(req.RefreshToken);
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
