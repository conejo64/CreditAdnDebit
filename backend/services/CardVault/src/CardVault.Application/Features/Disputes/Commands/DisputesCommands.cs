using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Application.Services;
using CardVault.Application.Contracts;

namespace CardVault.Application.Features.Disputes.Commands;

public record CloseDisputeCommand(Guid Id, bool Won) : IRequest<IResult>;
public class CloseDisputeCommandHandler : IRequestHandler<CloseDisputeCommand, IResult>
{
    private readonly DisputesService _disputes;

    public CloseDisputeCommandHandler(DisputesService disputes)
    {
        _disputes = disputes;
    }

    public async Task<IResult> Handle(CloseDisputeCommand request, CancellationToken cancellationToken)
    {
        var d = await _disputes.CloseAsync(request.Id, request.Won, cancellationToken);
        return Results.Ok(d);
    }
}

public record TransitionDisputeCommand(Guid Id, DisputeTransitionRequest Request) : IRequest<IResult>;
public class TransitionDisputeCommandHandler : IRequestHandler<TransitionDisputeCommand, IResult>
{
    private readonly DisputesService _disputes;

    public TransitionDisputeCommandHandler(DisputesService disputes)
    {
        _disputes = disputes;
    }

    public async Task<IResult> Handle(TransitionDisputeCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var d = await _disputes.TransitionAsync(request.Id, req.Action, req.Notes, cancellationToken);
        return Results.Ok(d);
    }
}
