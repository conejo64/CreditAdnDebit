using System.Diagnostics;
using System.Text.Json;
using CardVault.Api.Contracts;
using CardVault.Infrastructure.Persistence;
using CardVault.Infrastructure.Persistence.Loyalty;
using CardVault.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CardVault.Api.Services;

public sealed class LoyaltyService
{
    private const string LoyaltyEntryTopic = "cardvault.loyalty.entry.recorded";
    private const string LoyaltyRedeemedTopic = "cardvault.loyalty.reward.redeemed";

    private readonly CardVaultDbContext _db;
    private readonly AuditService _audit;

    public LoyaltyService(CardVaultDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<RewardProgramView> UpsertProgramAsync(UpsertRewardProgramRequest request, CancellationToken ct)
    {
        var productCode = request.ProductCode.Trim().ToUpperInvariant();
        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();

        var entity = await _db.RewardPrograms.FirstOrDefaultAsync(x =>
            x.ProductCode == productCode &&
            x.EffectiveDate == request.EffectiveDate, ct);

        if (entity is null)
        {
            entity = new RewardProgramEntity
            {
                Id = Guid.NewGuid(),
                ProductCode = productCode,
                EffectiveDate = request.EffectiveDate,
                CreatedOn = DateTimeOffset.UtcNow
            };
            _db.RewardPrograms.Add(entity);
        }

        entity.ProgramName = request.ProgramName.Trim();
        entity.CashbackRate = request.CashbackRate;
        entity.PointsPerCurrencyUnit = request.PointsPerCurrencyUnit;
        entity.CurrencyCode = currencyCode;
        entity.EndDate = request.EndDate;
        entity.IsActive = request.IsActive;
        entity.UpdatedOn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<RewardCatalogItemView> UpsertCatalogItemAsync(UpsertRewardCatalogItemRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var entity = await _db.RewardCatalogItems.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (entity is null)
        {
            entity = new RewardCatalogItemEntity
            {
                Id = Guid.NewGuid(),
                Code = code,
                CreatedOn = DateTimeOffset.UtcNow
            };
            _db.RewardCatalogItems.Add(entity);
        }

        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.PointsCost = request.PointsCost;
        entity.CashbackCost = request.CashbackCost;
        entity.Status = Enum.TryParse<RewardCatalogItemStatus>(request.Status, true, out var status)
            ? status
            : RewardCatalogItemStatus.Active;
        entity.UpdatedOn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<LoyaltyBalanceView> GetBalanceAsync(Guid accountId, CancellationToken ct)
    {
        var balance = await _db.LoyaltyBalances.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId, ct);
        return balance is null
            ? new LoyaltyBalanceView(accountId, 0m, 0m, DateTimeOffset.UtcNow)
            : Map(balance);
    }

    public async Task<IReadOnlyList<LoyaltyEntryView>> GetEntriesAsync(Guid accountId, int take, CancellationToken ct)
    {
        var limit = take <= 0 ? 50 : Math.Min(take, 200);
        var items = await _db.LoyaltyEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedOn)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<RewardProgramView>> GetProgramsAsync(CancellationToken ct)
    {
        var items = await _db.RewardPrograms.AsNoTracking()
            .OrderBy(x => x.ProductCode)
            .ThenByDescending(x => x.EffectiveDate)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<RewardCatalogItemView>> GetCatalogAsync(CancellationToken ct)
    {
        var items = await _db.RewardCatalogItems.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return items.Select(Map).ToList();
    }

    public async Task ApplyPurchaseRewardsAsync(Guid accountId, decimal amount, string sourceReference, string sourceType, CancellationToken ct)
    {
        if (amount <= 0m)
            return;

        var program = await ResolveProgramAsync(accountId, DateOnly.FromDateTime(DateTime.UtcNow.Date), ct);
        if (program is null)
            return;

        var exists = await _db.LoyaltyEntries.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.SourceType == sourceType && x.SourceReference == sourceReference && x.EntryType == LoyaltyEntryType.Accrual, ct);
        if (exists)
            return;

        var cashback = Math.Round(amount * program.CashbackRate, 2, MidpointRounding.AwayFromZero);
        var points = Math.Round(amount * program.PointsPerCurrencyUnit, 2, MidpointRounding.AwayFromZero);
        if (cashback == 0m && points == 0m)
            return;

        var balance = await GetOrCreateBalanceAsync(accountId, ct);
        balance.CashbackBalance += cashback;
        balance.PointsBalance += points;
        balance.UpdatedOn = DateTimeOffset.UtcNow;

        var entry = new LoyaltyEntryEntity
        {
            Id = Guid.NewGuid(),
            LoyaltyBalanceId = balance.Id,
            AccountId = accountId,
            EntryType = LoyaltyEntryType.Accrual,
            CashbackAmount = cashback,
            PointsAmount = points,
            SourceType = sourceType,
            SourceReference = sourceReference,
            Description = $"Rewards accrued for {sourceType}",
            CreatedOn = DateTimeOffset.UtcNow
        };

        _db.LoyaltyEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        await PublishEntryAsync(entry, program.CurrencyCode, LoyaltyEntryTopic, "loyalty.entry.recorded", ct);
    }

    public async Task ReversePurchaseRewardsAsync(Guid accountId, decimal amount, string sourceReference, string sourceType, CancellationToken ct)
    {
        if (amount <= 0m)
            return;

        var program = await ResolveProgramAsync(accountId, DateOnly.FromDateTime(DateTime.UtcNow.Date), ct);
        if (program is null)
            return;

        var exists = await _db.LoyaltyEntries.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.SourceType == sourceType && x.SourceReference == sourceReference && x.EntryType == LoyaltyEntryType.Reversal, ct);
        if (exists)
            return;

        var balance = await GetOrCreateBalanceAsync(accountId, ct);
        var cashbackToReverse = Math.Min(balance.CashbackBalance, Math.Round(amount * program.CashbackRate, 2, MidpointRounding.AwayFromZero));
        var pointsToReverse = Math.Min(balance.PointsBalance, Math.Round(amount * program.PointsPerCurrencyUnit, 2, MidpointRounding.AwayFromZero));
        if (cashbackToReverse == 0m && pointsToReverse == 0m)
            return;

        balance.CashbackBalance -= cashbackToReverse;
        balance.PointsBalance -= pointsToReverse;
        balance.UpdatedOn = DateTimeOffset.UtcNow;

        var entry = new LoyaltyEntryEntity
        {
            Id = Guid.NewGuid(),
            LoyaltyBalanceId = balance.Id,
            AccountId = accountId,
            EntryType = LoyaltyEntryType.Reversal,
            CashbackAmount = -cashbackToReverse,
            PointsAmount = -pointsToReverse,
            SourceType = sourceType,
            SourceReference = sourceReference,
            Description = $"Rewards reversed for {sourceType}",
            CreatedOn = DateTimeOffset.UtcNow
        };

        _db.LoyaltyEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        await PublishEntryAsync(entry, program.CurrencyCode, LoyaltyEntryTopic, "loyalty.entry.recorded", ct);
    }

    public async Task<LoyaltyBalanceView> RedeemRewardAsync(Guid accountId, RedeemRewardRequest request, CancellationToken ct)
    {
        var reward = await _db.RewardCatalogItems.FirstOrDefaultAsync(x => x.Id == request.RewardId, ct)
            ?? throw new InvalidOperationException("Reward catalog item not found.");

        if (reward.Status != RewardCatalogItemStatus.Active)
            throw new InvalidOperationException("Reward catalog item is not active.");

        var balance = await GetOrCreateBalanceAsync(accountId, ct);
        if (reward.PointsCost > balance.PointsBalance)
            throw new InvalidOperationException("Insufficient points balance.");
        if (reward.CashbackCost > balance.CashbackBalance)
            throw new InvalidOperationException("Insufficient cashback balance.");

        var sourceReference = string.IsNullOrWhiteSpace(request.RedemptionReference)
            ? reward.Id.ToString("N")
            : request.RedemptionReference.Trim();

        var exists = await _db.LoyaltyEntries.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.SourceType == "REDEMPTION" && x.SourceReference == sourceReference, ct);
        if (exists)
            throw new InvalidOperationException("Redemption reference already processed.");

        balance.PointsBalance -= reward.PointsCost;
        balance.CashbackBalance -= reward.CashbackCost;
        balance.UpdatedOn = DateTimeOffset.UtcNow;

        var entry = new LoyaltyEntryEntity
        {
            Id = Guid.NewGuid(),
            LoyaltyBalanceId = balance.Id,
            AccountId = accountId,
            EntryType = LoyaltyEntryType.Redemption,
            CashbackAmount = -reward.CashbackCost,
            PointsAmount = -reward.PointsCost,
            SourceType = "REDEMPTION",
            SourceReference = sourceReference,
            Description = $"Redeemed reward {reward.Code}",
            CreatedOn = DateTimeOffset.UtcNow
        };

        _db.LoyaltyEntries.Add(entry);
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = LoyaltyRedeemedTopic,
            Key = accountId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                accountId,
                rewardId = reward.Id,
                rewardCode = reward.Code,
                cashbackCost = reward.CashbackCost,
                pointsCost = reward.PointsCost,
                sourceReference,
                traceId = Activity.Current?.TraceId.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("loyalty.reward.redeemed", new
        {
            accountId,
            rewardId = reward.Id,
            rewardCode = reward.Code,
            cashbackCost = reward.CashbackCost,
            pointsCost = reward.PointsCost,
            sourceReference
        }, accountId.ToString("N"), Activity.Current?.TraceId.ToString(), ct);

        return Map(balance);
    }

    private async Task PublishEntryAsync(LoyaltyEntryEntity entry, string currencyCode, string topic, string eventType, CancellationToken ct)
    {
        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Topic = topic,
            Key = entry.AccountId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                entryId = entry.Id,
                accountId = entry.AccountId,
                entryType = entry.EntryType.ToString(),
                cashbackAmount = entry.CashbackAmount,
                pointsAmount = entry.PointsAmount,
                currencyCode,
                sourceType = entry.SourceType,
                sourceReference = entry.SourceReference,
                traceId = Activity.Current?.TraceId.ToString()
            })
        });

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(eventType, new
        {
            entryId = entry.Id,
            accountId = entry.AccountId,
            entryType = entry.EntryType.ToString(),
            cashbackAmount = entry.CashbackAmount,
            pointsAmount = entry.PointsAmount,
            sourceType = entry.SourceType,
            sourceReference = entry.SourceReference
        }, entry.AccountId.ToString("N"), Activity.Current?.TraceId.ToString(), ct);
    }

    private async Task<LoyaltyBalanceEntity> GetOrCreateBalanceAsync(Guid accountId, CancellationToken ct)
    {
        var balance = await _db.LoyaltyBalances.FirstOrDefaultAsync(x => x.AccountId == accountId, ct);
        if (balance is not null)
            return balance;

        balance = new LoyaltyBalanceEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CashbackBalance = 0m,
            PointsBalance = 0m,
            UpdatedOn = DateTimeOffset.UtcNow
        };

        _db.LoyaltyBalances.Add(balance);
        await _db.SaveChangesAsync(ct);
        return balance;
    }

    private async Task<RewardProgramEntity?> ResolveProgramAsync(Guid accountId, DateOnly businessDate, CancellationToken ct)
    {
        var productCode = await _db.Accounts.AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => x.ProductCode)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(productCode))
            return null;

        return await _db.RewardPrograms.AsNoTracking()
            .Where(x => x.ProductCode == productCode &&
                        x.IsActive &&
                        x.EffectiveDate <= businessDate &&
                        (x.EndDate == null || x.EndDate >= businessDate))
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    private static LoyaltyBalanceView Map(LoyaltyBalanceEntity entity)
        => new(entity.AccountId, decimal.Round(entity.CashbackBalance, 2), decimal.Round(entity.PointsBalance, 2), entity.UpdatedOn);

    private static LoyaltyEntryView Map(LoyaltyEntryEntity entity)
        => new(entity.Id, entity.EntryType.ToString(), decimal.Round(entity.CashbackAmount, 2), decimal.Round(entity.PointsAmount, 2), entity.SourceType, entity.SourceReference, entity.Description, entity.CreatedOn);

    private static RewardCatalogItemView Map(RewardCatalogItemEntity entity)
        => new(entity.Id, entity.Code, entity.Name, entity.Description, decimal.Round(entity.PointsCost, 2), decimal.Round(entity.CashbackCost, 2), entity.Status.ToString());

    private static RewardProgramView Map(RewardProgramEntity entity)
        => new(entity.Id, entity.ProductCode, entity.ProgramName, entity.CashbackRate, entity.PointsPerCurrencyUnit, entity.CurrencyCode, entity.IsActive, entity.EffectiveDate, entity.EndDate, entity.UpdatedOn);
}
