using System.Diagnostics;
using System.Text.Json;
using CardVault.Api.Contracts;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Accounting;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Issuer;
using CardVault.Infrastructure.Persistence.Outbox;
using CardVault.Infrastructure.Persistence.Settlement;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class AccountingService
{
    public const string PurchasePosted = "PURCHASE_POSTED";
    public const string PaymentApplied = "PAYMENT_APPLIED";
    public const string FeePosted = "FEE_POSTED";
    public const string InterestPosted = "INTEREST_POSTED";
    public const string RefundPosted = "REFUND_POSTED";
    public const string ReversalPosted = "REVERSAL_POSTED";
    public const string ChargebackPosted = "CHARGEBACK_POSTED";
    public const string ClearingPosted = "CLEARING_POSTED";
    public const string SettlementBatchPosted = "SETTLEMENT_BATCH_POSTED";

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public AccountingService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<LedgerAccountEntity> UpsertLedgerAccountAsync(UpsertLedgerAccountRequest request, CancellationToken ct)
    {
        var code = request.AccountCode.Trim().ToUpperInvariant();
        var entity = await _db.LedgerAccounts.FirstOrDefaultAsync(x => x.AccountCode == code, ct);
        if (entity is null)
        {
            entity = new LedgerAccountEntity
            {
                Id = Guid.NewGuid(),
                AccountCode = code
            };
            _db.LedgerAccounts.Add(entity);
        }

        entity.AccountName = request.AccountName.Trim();
        entity.AccountType = request.AccountType.Trim().ToUpperInvariant();
        entity.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        entity.Status = request.Status.Trim().ToUpperInvariant();

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<AccountingMappingEntity> UpsertMappingAsync(UpsertAccountingMappingRequest request, CancellationToken ct)
    {
        var eventType = request.EventType.Trim().ToUpperInvariant();
        var productCode = string.IsNullOrWhiteSpace(request.ProductCode) ? "*" : request.ProductCode.Trim().ToUpperInvariant();

        var entity = await _db.AccountingMappings.FirstOrDefaultAsync(x =>
            x.EventType == eventType &&
            x.ProductCode == productCode &&
            x.EffectiveDate == request.EffectiveDate, ct);

        if (entity is null)
        {
            entity = new AccountingMappingEntity
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                ProductCode = productCode,
                EffectiveDate = request.EffectiveDate
            };
            _db.AccountingMappings.Add(entity);
        }

        entity.DebitAccountCode = request.DebitAccountCode.Trim().ToUpperInvariant();
        entity.CreditAccountCode = request.CreditAccountCode.Trim().ToUpperInvariant();
        entity.EndDate = request.EndDate;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public Task<List<LedgerAccountEntity>> GetLedgerAccountsAsync(CancellationToken ct)
        => _db.LedgerAccounts.AsNoTracking().OrderBy(x => x.AccountCode).ToListAsync(ct);

    public Task<List<AccountingMappingEntity>> GetMappingsAsync(CancellationToken ct)
        => _db.AccountingMappings.AsNoTracking().OrderBy(x => x.EventType).ThenBy(x => x.ProductCode).ThenByDescending(x => x.EffectiveDate).ToListAsync(ct);

    public async Task<AccountingJournalEntryView?> GetJournalEntryAsync(Guid id, CancellationToken ct)
    {
        var entry = await _db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.LedgerAccount)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return entry is null ? null : Map(entry);
    }

    public async Task<IReadOnlyList<AccountingJournalEntryView>> GetJournalEntriesAsync(int take, CancellationToken ct)
    {
        var limit = take <= 0 ? 100 : Math.Min(take, 300);
        var entries = await _db.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .ThenInclude(x => x.LedgerAccount)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entries.Select(Map).ToList();
    }

    public async Task<JournalEntryEntity?> GenerateForLedgerEntryAsync(LedgerEntryEntity entry, string traceId, CancellationToken ct)
    {
        var eventType = MapLedgerEvent(entry.Type);
        if (eventType is null)
            return null;

        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entry.AccountId, ct);
        if (account is null)
            return null;

        var sourceReference = entry.Id.ToString("N");
        var existing = await _db.JournalEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourceModule == "LEDGER" && x.SourceReference == sourceReference && x.EventType == eventType, ct);
        if (existing is not null)
            return existing;

        var mapping = await ResolveMappingAsync(eventType, account.ProductCode, DateOnly.FromDateTime(entry.PostedOn.UtcDateTime), ct);
        if (mapping is null)
            return null;

        var debit = await GetLedgerAccountByCodeAsync(mapping.DebitAccountCode, ct);
        var credit = await GetLedgerAccountByCodeAsync(mapping.CreditAccountCode, ct);
        var amount = Math.Abs(entry.Amount);

        var journal = new JournalEntryEntity
        {
            Id = Guid.NewGuid(),
            BusinessDate = DateOnly.FromDateTime(entry.PostedOn.UtcDateTime),
            SourceModule = "LEDGER",
            SourceReference = sourceReference,
            EventType = eventType,
            Description = entry.Description,
            Status = "POSTED",
            TraceId = traceId,
            CreatedAt = DateTimeOffset.UtcNow,
            PostedAt = DateTimeOffset.UtcNow
        };

        journal.Lines.Add(BuildLine(journal.Id, debit, amount, 0m, debit.CurrencyCode, $"Debit {eventType}"));
        journal.Lines.Add(BuildLine(journal.Id, credit, 0m, amount, credit.CurrencyCode, $"Credit {eventType}"));

        _db.JournalEntries.Add(journal);
        await _db.SaveChangesAsync(ct);

        await PublishJournalAsync(journal, ct);
        return journal;
    }

    public async Task<JournalEntryEntity?> GenerateForSettlementBatchAsync(SettlementBatchEntity batch, string traceId, CancellationToken ct)
    {
        var sourceReference = batch.Id.ToString("N");
        var existing = await _db.JournalEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourceModule == "SETTLEMENT" && x.SourceReference == sourceReference && x.EventType == SettlementBatchPosted, ct);
        if (existing is not null)
            return existing;

        var mapping = await ResolveMappingAsync(SettlementBatchPosted, "*", batch.BusinessDate, ct);
        if (mapping is null)
            return null;

        var debit = await GetLedgerAccountByCodeAsync(mapping.DebitAccountCode, ct);
        var credit = await GetLedgerAccountByCodeAsync(mapping.CreditAccountCode, ct);
        var amount = Math.Abs(batch.GrossAmount);

        var journal = new JournalEntryEntity
        {
            Id = Guid.NewGuid(),
            BusinessDate = batch.BusinessDate,
            SourceModule = "SETTLEMENT",
            SourceReference = sourceReference,
            EventType = SettlementBatchPosted,
            Description = $"Settlement batch {batch.Network} {batch.BusinessDate:yyyy-MM-dd}",
            Status = "POSTED",
            TraceId = traceId,
            CreatedAt = DateTimeOffset.UtcNow,
            PostedAt = DateTimeOffset.UtcNow
        };

        journal.Lines.Add(BuildLine(journal.Id, debit, amount, 0m, debit.CurrencyCode, "Debit settlement batch"));
        journal.Lines.Add(BuildLine(journal.Id, credit, 0m, amount, credit.CurrencyCode, "Credit settlement batch"));

        _db.JournalEntries.Add(journal);
        await _db.SaveChangesAsync(ct);

        await PublishJournalAsync(journal, ct);
        return journal;
    }

    public static string? MapLedgerEvent(LedgerEntryType type) => type switch
    {
        LedgerEntryType.Purchase => PurchasePosted,
        LedgerEntryType.Payment => PaymentApplied,
        LedgerEntryType.Fee => FeePosted,
        LedgerEntryType.Interest => InterestPosted,
        LedgerEntryType.Refund => RefundPosted,
        LedgerEntryType.Reversal => ReversalPosted,
        LedgerEntryType.Chargeback => ChargebackPosted,
        LedgerEntryType.Clearing => ClearingPosted,
        _ => null
    };

    private async Task<AccountingMappingEntity?> ResolveMappingAsync(string eventType, string productCode, DateOnly businessDate, CancellationToken ct)
    {
        var normalizedProduct = string.IsNullOrWhiteSpace(productCode) ? "*" : productCode.Trim().ToUpperInvariant();

        return await _db.AccountingMappings.AsNoTracking()
            .Where(x =>
                x.EventType == eventType &&
                (x.ProductCode == normalizedProduct || x.ProductCode == "*") &&
                x.EffectiveDate <= businessDate &&
                (x.EndDate == null || x.EndDate >= businessDate))
            .OrderByDescending(x => x.ProductCode == normalizedProduct)
            .ThenByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<LedgerAccountEntity> GetLedgerAccountByCodeAsync(string accountCode, CancellationToken ct)
        => await _db.LedgerAccounts.AsNoTracking().FirstAsync(x => x.AccountCode == accountCode, ct);

    private async Task PublishJournalAsync(JournalEntryEntity journal, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            journalEntryId = journal.Id,
            businessDate = journal.BusinessDate.ToString("yyyy-MM-dd"),
            sourceModule = journal.SourceModule,
            sourceReference = journal.SourceReference,
            eventType = journal.EventType,
            description = journal.Description,
            status = journal.Status,
            traceId = journal.TraceId,
            postedAt = journal.PostedAt
        });

        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = "accounting.v1.journal.posted",
            Key = journal.Id.ToString("N"),
            PayloadJson = payload
        });

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("accounting.journal.posted", new
        {
            journalEntryId = journal.Id,
            businessDate = journal.BusinessDate.ToString("yyyy-MM-dd"),
            sourceModule = journal.SourceModule,
            sourceReference = journal.SourceReference,
            eventType = journal.EventType,
            status = journal.Status,
            traceId = journal.TraceId
        }, journal.Id.ToString("N"), journal.TraceId ?? Activity.Current?.TraceId.ToString(), ct);
    }

    private static JournalEntryLineEntity BuildLine(Guid journalId, LedgerAccountEntity account, decimal debit, decimal credit, string currencyCode, string description)
        => new()
        {
            Id = Guid.NewGuid(),
            JournalEntryId = journalId,
            LedgerAccountId = account.Id,
            DebitAmount = debit,
            CreditAmount = credit,
            CurrencyCode = currencyCode,
            Description = description
        };

    private static AccountingJournalEntryView Map(JournalEntryEntity entry)
        => new(
            entry.Id,
            entry.BusinessDate,
            entry.SourceModule,
            entry.SourceReference,
            entry.EventType,
            entry.Description,
            entry.Status,
            entry.TraceId,
            entry.CreatedAt,
            entry.PostedAt,
            entry.Lines.Select(line => new AccountingJournalLineView(
                line.Id,
                line.LedgerAccount.AccountCode,
                line.LedgerAccount.AccountName,
                line.DebitAmount,
                line.CreditAmount,
                line.CurrencyCode,
                line.Description)).ToList());
}
