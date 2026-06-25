using CardVault.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace CardVault.Application.Features.Accounting.Queries;

public record GetLedgerAccountsQuery() : IRequest<IResult>;
public class GetLedgerAccountsQueryHandler : IRequestHandler<GetLedgerAccountsQuery, IResult>
{
    private readonly AccountingService _service;

    public GetLedgerAccountsQueryHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetLedgerAccountsQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetLedgerAccountsAsync(cancellationToken));
}

public record GetAccountingMappingsQuery() : IRequest<IResult>;
public class GetAccountingMappingsQueryHandler : IRequestHandler<GetAccountingMappingsQuery, IResult>
{
    private readonly AccountingService _service;

    public GetAccountingMappingsQueryHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetAccountingMappingsQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetMappingsAsync(cancellationToken));
}

public record GetAccountingJournalEntriesQuery(int Take) : IRequest<IResult>;
public class GetAccountingJournalEntriesQueryHandler : IRequestHandler<GetAccountingJournalEntriesQuery, IResult>
{
    private readonly AccountingService _service;

    public GetAccountingJournalEntriesQueryHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetAccountingJournalEntriesQuery request, CancellationToken cancellationToken)
        => Results.Ok(await _service.GetJournalEntriesAsync(request.Take, cancellationToken));
}

public record GetAccountingJournalEntryQuery(Guid Id) : IRequest<IResult>;
public class GetAccountingJournalEntryQueryHandler : IRequestHandler<GetAccountingJournalEntryQuery, IResult>
{
    private readonly AccountingService _service;

    public GetAccountingJournalEntryQueryHandler(AccountingService service)
    {
        _service = service;
    }

    public async Task<IResult> Handle(GetAccountingJournalEntryQuery request, CancellationToken cancellationToken)
    {
        var item = await _service.GetJournalEntryAsync(request.Id, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
}
