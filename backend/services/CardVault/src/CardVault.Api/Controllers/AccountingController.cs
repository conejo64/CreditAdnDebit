using CardVault.Api.Contracts;
using CardVault.Api.Features.Accounting.Commands;
using CardVault.Api.Features.Accounting.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CardVault.Api.Controllers;

[ApiController]
[Route("api/accounting")]
[Authorize(Policy = "CanViewAccounting")]
public class AccountingController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("journal-entries")]
    [Authorize(Policy = "CanViewAccounting")]
    public async Task<IResult> GetJournalEntries([FromQuery] int take, CancellationToken ct)
        => await _mediator.Send(new GetAccountingJournalEntriesQuery(take), ct);

    [HttpGet("journal-entries/{id:guid}")]
    [Authorize(Policy = "CanViewAccounting")]
    public async Task<IResult> GetJournalEntry(Guid id, CancellationToken ct)
        => await _mediator.Send(new GetAccountingJournalEntryQuery(id), ct);

    [HttpGet("ledger-accounts")]
    [Authorize(Policy = "CanViewAccounting")]
    public async Task<IResult> GetLedgerAccounts(CancellationToken ct)
        => await _mediator.Send(new GetLedgerAccountsQuery(), ct);

    [HttpPost("ledger-accounts")]
    [Authorize(Policy = "CanManageAccounting")]
    public async Task<IResult> UpsertLedgerAccount([FromBody] UpsertLedgerAccountRequest request, CancellationToken ct)
        => await _mediator.Send(new UpsertLedgerAccountCommand(request), ct);

    [HttpGet("mappings")]
    [Authorize(Policy = "CanViewAccounting")]
    public async Task<IResult> GetMappings(CancellationToken ct)
        => await _mediator.Send(new GetAccountingMappingsQuery(), ct);

    [HttpPost("mappings")]
    [Authorize(Policy = "CanManageAccounting")]
    public async Task<IResult> UpsertMapping([FromBody] UpsertAccountingMappingRequest request, CancellationToken ct)
        => await _mediator.Send(new UpsertAccountingMappingCommand(request), ct);
}
