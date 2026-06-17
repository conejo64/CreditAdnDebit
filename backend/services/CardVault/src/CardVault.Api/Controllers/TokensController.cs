using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using CardVault.Application.Contracts;
using CardVault.Api.Vault;
using CardVault.Application.Features.Tokens.Commands;
using CardVault.Application.Features.Tokens.Queries;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/tokens")]
public class TokensController : ControllerBase
{
    private readonly IMediator _mediator;

    public TokensController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("tokenize")]
    [Authorize(Policy = "CanManageCards")]
    public async Task<IResult> Tokenize([FromBody] TokenizeRequest req, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        var traceId = HttpContext.TraceIdentifier;
        return await _mediator.Send(new TokenizeCommand(req.Pan, req.ExpiryYyMm, actor, traceId), ct);
    }

    [HttpPost("detokenize")]
    [Authorize(Policy = "CanDetokenize")]
    [EnableRateLimiting("vault_detokenize")]
    public async Task<IResult> Detokenize([FromQuery] string token, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        var traceId = HttpContext.TraceIdentifier;
        return await _mediator.Send(new DetokenizeCommand(token, actor, traceId), ct);
    }

    [HttpGet("{token}/metadata")]
    [Authorize(Policy = "CanManageCards")]
    public async Task<IResult> GetMetadata(string token, CancellationToken ct)
    {
        return await _mediator.Send(new GetTokenMetadataQuery(token), ct);
    }
}

[ApiController]
[Route("api/vault")]
public class VaultController : ControllerBase
{
    private readonly IMediator _mediator;

    public VaultController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("active-key")]
    [Authorize(Policy = "CanViewAudit")]
    public async Task<IResult> GetActiveKey(CancellationToken ct)
    {
        return await _mediator.Send(new GetActiveKeyQuery(), ct);
    }

    [HttpPost("rotate-active-key")]
    [Authorize(Policy = "CanRotateVaultKeys")]
    [EnableRateLimiting("vault_admin_ops")]
    public async Task<IResult> RotateActiveKey([FromQuery] string keyId, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        var traceId = HttpContext.TraceIdentifier;
        return await _mediator.Send(new RotateActiveKeyCommand(keyId, actor, traceId), ct);
    }

    [HttpPost("reencrypt")]
    [Authorize(Policy = "CanRotateVaultKeys")]
    [EnableRateLimiting("vault_admin_ops")]
    public async Task<IResult> ReEncrypt([FromQuery] int take, CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        var traceId = HttpContext.TraceIdentifier;
        return await _mediator.Send(new ReEncryptBatchCommand(take, actor, traceId), ct);
    }
}

[ApiController]
[Route("api/demo")]
public class DemoController : ControllerBase
{
    private readonly IMediator _mediator;

    public DemoController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("publish")]
    [Authorize]
    public async Task<IResult> Publish([FromBody] DemoPublishRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue("uid") ?? "unknown";
        return await _mediator.Send(new DemoPublishCommand(req, userId), ct);
    }
}

[ApiController]
[Route("api/audit")]
[Authorize(Policy = "CanViewAudit")]
public class AuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("latest")]
    public async Task<IResult> GetLatest([FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetAuditLatestQuery(take), ct);
    }
}

[ApiController]
[Route("api/outbox")]
[Authorize(Policy = "CanViewAudit")]
public class OutboxController : ControllerBase
{
    private readonly IMediator _mediator;

    public OutboxController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("latest")]
    public async Task<IResult> GetLatest([FromQuery] int take, CancellationToken ct)
    {
        return await _mediator.Send(new GetOutboxLatestQuery(take), ct);
    }
}
