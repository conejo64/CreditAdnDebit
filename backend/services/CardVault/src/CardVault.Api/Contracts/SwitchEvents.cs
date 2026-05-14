namespace CardVault.Api.Contracts;

public sealed record SwitchPurchaseApprovedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, DateTimeOffset PostedOn);

public sealed record SwitchPurchaseReversedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, DateTimeOffset PostedOn);

public sealed record SwitchRefundPostedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, DateTimeOffset PostedOn);

public sealed record SwitchChargebackPostedV1(Guid AccountId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? ReasonCode, DateTimeOffset PostedOn);

public sealed record SwitchAuthApprovedV1(Guid AccountId, Guid? CardId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, string? MerchantId, string? MerchantCategory, string? CountryCode, string? PinBlock, DateTimeOffset PostedOn);

public sealed record SwitchAuthReversedV1(Guid AccountId, Guid? CardId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, string? MerchantId, string? MerchantCategory, string? CountryCode, string? PinBlock, DateTimeOffset PostedOn);

public sealed record SwitchClearingPostedV1(Guid AccountId, Guid? CardId, decimal Amount, string Network, string Mti, string Stan, string Rrn, string? OriginalDataElements90, string? MerchantId, string? MerchantCategory, string? CountryCode, string? PinBlock, DateTimeOffset PostedOn);
