using MediatR;
using Microsoft.AspNetCore.Http;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Api.Services;

namespace CardVault.Api.Features.Ledger.Commands;

public record PostLedgerCommand(PostLedgerRequest Request, LedgerEntryType Type) : IRequest<IResult>;
public class PostLedgerCommandHandler : IRequestHandler<PostLedgerCommand, IResult>
{
    private readonly LedgerService _ledger;

    public PostLedgerCommandHandler(LedgerService ledger)
    {
        _ledger = ledger;
    }

    public async Task<IResult> Handle(PostLedgerCommand request, CancellationToken cancellationToken)
    {
        var req = request.Request;
        var e = await _ledger.AddEntryAsync(req.AccountId, request.Type, req.Amount, req.Description, req.PostedOn ?? DateTimeOffset.UtcNow, cancellationToken);
        return Results.Created($"/api/ledger/entries/{e.Id}", e);
    }
}
