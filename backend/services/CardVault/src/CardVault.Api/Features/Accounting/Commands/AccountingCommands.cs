using CardVault.Api.Contracts;
using CardVault.Api.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Api.Features.Accounting.Commands;

public record UpsertLedgerAccountCommand(UpsertLedgerAccountRequest Request) : IRequest<IResult>;
public class UpsertLedgerAccountCommandHandler : IRequestHandler<UpsertLedgerAccountCommand, IResult>
{
    private readonly AccountingService _service;

    public UpsertLedgerAccountCommandHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(UpsertLedgerAccountCommand request, CancellationToken cancellationToken)
    {
        var saved = await _service.UpsertLedgerAccountAsync(request.Request, cancellationToken);
        return Results.Ok(saved);
    }
}

public record UpsertAccountingMappingCommand(UpsertAccountingMappingRequest Request) : IRequest<IResult>;
public class UpsertAccountingMappingCommandHandler : IRequestHandler<UpsertAccountingMappingCommand, IResult>
{
    private readonly AccountingService _service;

    public UpsertAccountingMappingCommandHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(UpsertAccountingMappingCommand request, CancellationToken cancellationToken)
    {
        var saved = await _service.UpsertMappingAsync(request.Request, cancellationToken);
        return Results.Ok(saved);
    }
}
