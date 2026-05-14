using CardVault.Api.Contracts;
using CardVault.Api.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Api.Features.Ecommerce3ds.Commands;

public record StartThreeDsChallengeCommand(StartThreeDsChallengeRequest Request, string Actor, string TraceId) : IRequest<IResult>;

public class StartThreeDsChallengeCommandHandler : IRequestHandler<StartThreeDsChallengeCommand, IResult>
{
    private readonly ThreeDsService _service;

    public StartThreeDsChallengeCommandHandler(ThreeDsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(StartThreeDsChallengeCommand request, CancellationToken cancellationToken)
    {
        var response = await _service.StartChallengeAsync(request.Request, request.Actor, request.TraceId, cancellationToken);
        return Results.Created($"/api/ecommerce/3ds/challenges/{response.ChallengeId}", response);
    }
}

public record VerifyThreeDsChallengeCommand(Guid ChallengeId, VerifyThreeDsChallengeRequest Request, string Actor, string TraceId) : IRequest<IResult>;

public class VerifyThreeDsChallengeCommandHandler : IRequestHandler<VerifyThreeDsChallengeCommand, IResult>
{
    private readonly ThreeDsService _service;

    public VerifyThreeDsChallengeCommandHandler(ThreeDsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(VerifyThreeDsChallengeCommand request, CancellationToken cancellationToken)
    {
        var response = await _service.VerifyChallengeAsync(request.ChallengeId, request.Request.Otp, request.Actor, request.TraceId, cancellationToken);
        return Results.Ok(response);
    }
}
