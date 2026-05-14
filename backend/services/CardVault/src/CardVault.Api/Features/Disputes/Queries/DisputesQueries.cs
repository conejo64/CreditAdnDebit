using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Disputes.Queries;

public record GetDisputesQuery(Guid AccountId, int Take) : IRequest<IResult>;
public class GetDisputesQueryHandler : IRequestHandler<GetDisputesQuery, IResult>
{
    private readonly DisputesService _disputes;

    public GetDisputesQueryHandler(DisputesService disputes)
    {
        _disputes = disputes;
    }

    public async Task<IResult> Handle(GetDisputesQuery request, CancellationToken cancellationToken)
    {
        var list = await _disputes.ListAsync(request.AccountId, request.Take <= 0 ? 50 : request.Take, cancellationToken);
        return Results.Ok(list);
    }
}

public record GetDisputeEventsQuery(Guid Id, int Take) : IRequest<IResult>;
public class GetDisputeEventsQueryHandler : IRequestHandler<GetDisputeEventsQuery, IResult>
{
    private readonly DisputesService _disputes;

    public GetDisputeEventsQueryHandler(DisputesService disputes)
    {
        _disputes = disputes;
    }

    public async Task<IResult> Handle(GetDisputeEventsQuery request, CancellationToken cancellationToken)
    {
        var list = await _disputes.ListEventsAsync(request.Id, request.Take <= 0 ? 50 : request.Take, cancellationToken);
        return Results.Ok(list);
    }
}
