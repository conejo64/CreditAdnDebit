using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using CardVault.Application.Contracts;
using CardVault.Application.Features.Auth.Commands;
using CardVault.Api.Security;
using System.Security.Claims;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("register")]
    [Authorize(Policy = "CanManageUsersRoles")]
    public async Task<IResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new RegisterUserCommand(req), ct);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new LoginCommand(req), ct);
        return AuthCookieWriter.ApplyCookies(HttpContext, result);
    }

    [HttpPost("mfa/enable")]
    [AllowAnonymous]
    public async Task<IResult> MfaEnable([FromBody] MfaEnableRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new MfaEnableCommand(req), ct);
    }

    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    public async Task<IResult> MfaVerify([FromBody] MfaVerifyRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new MfaVerifyCommand(req), ct);
        return AuthCookieWriter.ApplyCookies(HttpContext, result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IResult> Refresh([FromBody] RefreshRequest? req, CancellationToken ct)
    {
        // SEC-03: the cv_rt cookie is the primary source; the (now-optional) body field
        // is kept only as a fallback so the contract stays backward-compatible.
        var refreshToken = Request.Cookies[AuthCookieNames.RefreshToken] ?? req?.RefreshToken;
        var result = await _mediator.Send(new RefreshTokenCommand(new RefreshRequest(refreshToken)), ct);
        return AuthCookieWriter.ApplyCookies(HttpContext, result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieNames.RefreshToken];
        var result = await _mediator.Send(new LogoutCommand(refreshToken), ct);
        AuthCookieWriter.ClearCookies(Response);
        return result;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IResult> Me(CancellationToken ct)
    {
        var userId =
            User.FindFirstValue("uid") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        return await _mediator.Send(new GetCurrentUserQuery(userId), ct);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth_password_reset")]
    public async Task<IResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new ForgotPasswordCommand(req), ct);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth_password_reset")]
    public async Task<IResult> ResetPassword([FromBody] ResetPasswordByTokenRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new ResetPasswordCommand(req), ct);
    }
}
