using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Application.Services;
using CardVault.Application.Contracts;
using CardVault.Application.Ports;

namespace CardVault.Application.Features.Tokens.Commands;

public record TokenizeCommand(string Pan, string? ExpiryYyMm, string Actor, string TraceId) : IRequest<IResult>;
public class TokenizeCommandHandler : IRequestHandler<TokenizeCommand, IResult>
{
    private readonly IPanVault _svc;

    public TokenizeCommandHandler(IPanVault svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(TokenizeCommand request, CancellationToken cancellationToken)
    {
        var res = await _svc.TokenizeAsync(request.Pan, request.ExpiryYyMm, request.Actor, request.TraceId, cancellationToken);
        return Results.Ok(res);
    }
}

public record DetokenizeCommand(string Token, string Actor, string TraceId) : IRequest<IResult>;
public class DetokenizeCommandHandler : IRequestHandler<DetokenizeCommand, IResult>
{
    private readonly IPanVault _svc;

    public DetokenizeCommandHandler(IPanVault svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(DetokenizeCommand request, CancellationToken cancellationToken)
    {
        var res = await _svc.DetokenizeAsync(request.Token, request.Actor, request.TraceId, cancellationToken);
        return Results.Ok(res);
    }
}

public record RotateActiveKeyCommand(string KeyId, string Actor, string TraceId) : IRequest<IResult>;
public class RotateActiveKeyCommandHandler : IRequestHandler<RotateActiveKeyCommand, IResult>
{
    private readonly IPanVault _svc;

    public RotateActiveKeyCommandHandler(IPanVault svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(RotateActiveKeyCommand request, CancellationToken cancellationToken)
    {
        var res = await _svc.RotateActiveKeyAsync(request.KeyId, request.Actor, request.TraceId, cancellationToken);
        return Results.Ok(res);
    }
}

public record ReEncryptBatchCommand(int Take, string Actor, string TraceId) : IRequest<IResult>;
public class ReEncryptBatchCommandHandler : IRequestHandler<ReEncryptBatchCommand, IResult>
{
    private readonly IPanVault _svc;

    public ReEncryptBatchCommandHandler(IPanVault svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(ReEncryptBatchCommand request, CancellationToken cancellationToken)
    {
        var res = await _svc.ReEncryptBatchAsync(request.Take, request.Actor, request.TraceId, cancellationToken);
        return Results.Ok(res);
    }
}

public record DemoPublishCommand(DemoPublishRequest Request, string UserId) : IRequest<IResult>;
public class DemoPublishCommandHandler : IRequestHandler<DemoPublishCommand, IResult>
{
    private readonly CardVaultDbContext _db;

    public DemoPublishCommandHandler(CardVaultDbContext db)
    {
        _db = db;
    }

    public async Task<IResult> Handle(DemoPublishCommand request, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { type = "cv.demo", message = request.Request.Message, userId = request.UserId, atUtc = DateTimeOffset.UtcNow });
        _db.OutboxMessages.Add(new OutboxMessageEntity { Topic = "cv.demo", Key = Guid.NewGuid().ToString("N"), PayloadJson = payload });
        await _db.SaveChangesAsync(cancellationToken);
        return Results.Accepted();
    }
}
