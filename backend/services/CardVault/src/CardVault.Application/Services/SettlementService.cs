using CardVault.Application.Contracts;
using CardVault.Domain;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Billing;
using CardVault.Infrastructure.Persistence.Settlement;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Application.Services;

/// <summary>
/// v34 simplified daily settlement by network.
/// In real life, clearing comes from network files; here we aggregate posted purchases for a business date.
/// </summary>
public sealed class SettlementService
{
    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;
    private readonly AccountingService _accounting;

    public SettlementService(CardVaultDbContext db, AuditService audit, AccountingService accounting)
    {
        _db = db;
        _audit = audit;
        _accounting = accounting;
    }

    public static SettlementNetwork MapNetwork(string network) =>
        network.ToLowerInvariant() switch
        {
            "visa" => SettlementNetwork.Visa,
            "mastercard" or "mc" => SettlementNetwork.MasterCard,
            "discover" => SettlementNetwork.Discover,
            "diners" or "dinersclub" => SettlementNetwork.Diners,
            _ => SettlementNetwork.Other
        };

    public async Task<SettlementBatchEntity> CreateOrGetBatchAsync(SettlementNetwork network, DateOnly businessDate, CancellationToken ct)
    {
        var existing = await _db.SettlementBatches
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Network == network && x.BusinessDate == businessDate, ct);

        if (existing is not null) return existing;

        var b = new SettlementBatchEntity
        {
            Id = Guid.NewGuid(),
            Network = network,
            BusinessDate = businessDate,
            Status = SettlementBatchStatus.Open,
            CreatedOn = DateTimeOffset.UtcNow
        };
        _db.SettlementBatches.Add(b);
        await _db.SaveChangesAsync(ct);
        return b;
    }

    public async Task<SettlementBatchEntity> RunDailySettlementAsync(string network, DateOnly businessDate, CancellationToken ct)
    {
        var net = MapNetwork(network);

        var batch = await CreateOrGetBatchAsync(net, businessDate, ct);
        if (batch.Status != SettlementBatchStatus.Open)
            throw new InvalidOperationException("Batch is not open");

        // Find ledger purchases posted on that date, with description containing "SWITCH PURCHASE {network}"
        var dayStart = businessDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = businessDate.ToDateTime(TimeOnly.MaxValue);

        var purchases = await _db.LedgerEntries.AsNoTracking()
            .Where(x => x.Type == LedgerEntryType.Purchase &&
                        x.PostedOn >= dayStart && x.PostedOn <= dayEnd &&
                        x.Description.Contains($"SWITCH PURCHASE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.PostedOn)
            .ToListAsync(ct);

        // naive filter by network string in description
        purchases = purchases.Where(p => p.Description.Contains(network, StringComparison.OrdinalIgnoreCase)).ToList();

        // Avoid duplicates: only include ledger entries not already in any settlement item.
        var already = await _db.SettlementItems.AsNoTracking()
            .Where(i => i.Batch.Network == net && i.Batch.BusinessDate == businessDate)
            .Select(i => i.LedgerEntryId)
            .ToListAsync(ct);

        var toAdd = purchases.Where(p => !already.Contains(p.Id)).ToList();

        foreach (var p in toAdd)
        {
            var rrn = ExtractRrn(p.Description) ?? p.Id.ToString("N")[..12];

            _db.SettlementItems.Add(new SettlementItemEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                LedgerEntryId = p.Id,
                AccountId = p.AccountId,
                Amount = p.Amount,
                NetworkRef = rrn,
                PostedOn = p.PostedOn
            });
        }

        batch.TxnCount += toAdd.Count;
        batch.GrossAmount += toAdd.Sum(x => x.Amount);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(EventNames.SettlementBatchCreatedV1,
            new { batchId = batch.Id, network = batch.Network.ToString(), businessDate = batch.BusinessDate.ToString(), added = toAdd.Count, gross = batch.GrossAmount },
            correlationId: null,
            traceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ct: ct);

        await _accounting.GenerateForSettlementBatchAsync(batch, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "settlement", ct);

        return await _db.SettlementBatches.Include(x => x.Items).FirstAsync(x => x.Id == batch.Id, ct);
    }

    public Task<List<SettlementBatchEntity>> GetBatchesAsync(int take, CancellationToken ct) =>
        _db.SettlementBatches.AsNoTracking()
            .OrderByDescending(x => x.BusinessDate)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    public Task<SettlementBatchEntity?> GetBatchAsync(Guid id, CancellationToken ct) =>
        _db.SettlementBatches.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    private static string? ExtractRrn(string description)
    {
        var idx = description.IndexOf("RRN:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var s = description[(idx + 4)..].Trim();
        return s.Length > 64 ? s[..64] : s;
    }
}
