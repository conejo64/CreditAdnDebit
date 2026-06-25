namespace CardVault.Application.Contracts;

public sealed record UpsertLedgerAccountRequest(
    string AccountCode,
    string AccountName,
    string AccountType,
    string CurrencyCode,
    string Status);

public sealed record UpsertAccountingMappingRequest(
    string EventType,
    string ProductCode,
    string DebitAccountCode,
    string CreditAccountCode,
    DateOnly EffectiveDate,
    DateOnly? EndDate);

public sealed record AccountingJournalLineView(
    Guid LineId,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    string CurrencyCode,
    string Description);

public sealed record AccountingJournalEntryView(
    Guid JournalEntryId,
    DateOnly BusinessDate,
    string SourceModule,
    string SourceReference,
    string EventType,
    string Description,
    string Status,
    string? TraceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt,
    IReadOnlyList<AccountingJournalLineView> Lines);
