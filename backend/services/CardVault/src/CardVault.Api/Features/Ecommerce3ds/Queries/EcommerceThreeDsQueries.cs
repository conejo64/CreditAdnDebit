using CardVault.Api.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Api.Features.Ecommerce3ds.Queries;

public record GetThreeDsChallengeQuery(Guid ChallengeId) : IRequest<IResult>;

public class GetThreeDsChallengeQueryHandler : IRequestHandler<GetThreeDsChallengeQuery, IResult>
{
    private readonly ThreeDsService _service;

    public GetThreeDsChallengeQueryHandler(ThreeDsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetThreeDsChallengeQuery request, CancellationToken cancellationToken)
    {
        var challenge = await _service.GetChallengeAsync(request.ChallengeId, cancellationToken);
        return challenge is null ? Results.NotFound() : Results.Ok(challenge);
    }
}

public record ListThreeDsChallengesQuery(string? Status, int Take) : IRequest<IResult>;

public class ListThreeDsChallengesQueryHandler : IRequestHandler<ListThreeDsChallengesQuery, IResult>
{
    private readonly ThreeDsService _service;

    public ListThreeDsChallengesQueryHandler(ThreeDsService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(ListThreeDsChallengesQuery request, CancellationToken cancellationToken)
    {
        var items = await _service.ListChallengesAsync(request.Status, request.Take, cancellationToken);
        return Results.Ok(items);
    }
}
