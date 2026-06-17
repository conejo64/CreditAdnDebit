namespace CardVault.Application.Contracts;

public sealed record UpsertRewardProgramRequest(
    string ProductCode,
    string ProgramName,
    decimal CashbackRate,
    decimal PointsPerCurrencyUnit,
    string CurrencyCode,
    DateOnly EffectiveDate,
    DateOnly? EndDate,
    bool IsActive);

public sealed record UpsertRewardCatalogItemRequest(
    string Code,
    string Name,
    string Description,
    decimal PointsCost,
    decimal CashbackCost,
    string Status);

public sealed record RedeemRewardRequest(
    Guid RewardId,
    string? RedemptionReference);

public sealed record LoyaltyBalanceView(
    Guid AccountId,
    decimal CashbackBalance,
    decimal PointsBalance,
    DateTimeOffset UpdatedOn);

public sealed record LoyaltyEntryView(
    Guid EntryId,
    string EntryType,
    decimal CashbackAmount,
    decimal PointsAmount,
    string SourceType,
    string SourceReference,
    string Description,
    DateTimeOffset CreatedOn);

public sealed record RewardCatalogItemView(
    Guid RewardId,
    string Code,
    string Name,
    string Description,
    decimal PointsCost,
    decimal CashbackCost,
    string Status);

public sealed record RewardProgramView(
    Guid ProgramId,
    string ProductCode,
    string ProgramName,
    decimal CashbackRate,
    decimal PointsPerCurrencyUnit,
    string CurrencyCode,
    bool IsActive,
    DateOnly EffectiveDate,
    DateOnly? EndDate,
    DateTimeOffset UpdatedOn);
