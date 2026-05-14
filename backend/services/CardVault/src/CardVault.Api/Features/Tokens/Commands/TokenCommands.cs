using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Api.Services;
using CardVault.Api.Contracts;
using CardVault.Api.Vault;
using CardVault.Api.Vault;

namespace CardVault.Api.Features.Tokens.Commands;

public record TokenizeCommand(TokenizeRequest Request, string Actor, string TraceId) : IRequest<IResult>;
public class TokenizeCommandHandler : IRequestHandler<TokenizeCommand, IResult>
{
    private readonly TokenVaultService _svc;

    public TokenizeCommandHandler(TokenVaultService svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(TokenizeCommand request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = CardVault.Api.Observability.ActivitySource.StartActivity("vault.tokenize");
        activity?.SetTag("traceId", request.TraceId);
        
        var res = await _svc.TokenizeAsync(request.Request, request.Actor, request.TraceId, cancellationToken);
        
        sw.Stop();
        CardVault.Api.Observability.VaultOperationsTotal.Add(1, new KeyValuePair<string, object?>("op", "tokenize"));
        CardVault.Api.Observability.VaultOperationDurationMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("op", "tokenize"));
        activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
        
        return Results.Ok(res);
    }
}

public record DetokenizeCommand(string Token, string Actor, string TraceId) : IRequest<IResult>;
public class DetokenizeCommandHandler : IRequestHandler<DetokenizeCommand, IResult>
{
    private readonly TokenVaultService _svc;

    public DetokenizeCommandHandler(TokenVaultService svc)
    {
        _svc = svc;
    }

    public async Task<IResult> Handle(DetokenizeCommand request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = CardVault.Api.Observability.ActivitySource.StartActivity("vault.detokenize");
        activity?.SetTag("traceId", request.TraceId);
        
        var res = await _svc.DetokenizeAsync(request.Token, request.Actor, request.TraceId, cancellationToken);
        
        sw.Stop();
        CardVault.Api.Observability.VaultOperationsTotal.Add(1, new KeyValuePair<string, object?>("op", "detokenize"));
        CardVault.Api.Observability.VaultOperationDurationMs.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("op", "detokenize"));
        activity?.SetTag("duration_ms", sw.Elapsed.TotalMilliseconds);
        
        return Results.Ok(res);
    }
}

public record RotateActiveKeyCommand(string KeyId, string Actor, string TraceId) : IRequest<IResult>;
public class RotateActiveKeyCommandHandler : IRequestHandler<RotateActiveKeyCommand, IResult>
{
    private readonly TokenVaultService _svc;

    public RotateActiveKeyCommandHandler(TokenVaultService svc)
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
    private readonly TokenVaultService _svc;

    public ReEncryptBatchCommandHandler(TokenVaultService svc)
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
