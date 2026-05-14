using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using CardVault.Api.Contracts;
using CardVault.Api.Features.Auth.Commands;
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
    [AllowAnonymous]
    public async Task<IResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new RegisterUserCommand(req), ct);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new LoginCommand(req), ct);
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
        return await _mediator.Send(new MfaVerifyCommand(req), ct);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        return await _mediator.Send(new RefreshTokenCommand(req), ct);
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
}
