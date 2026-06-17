namespace CardVault.Application.Contracts;

public sealed record RegisterWalletTokenRequest(
    Guid CardId,
    string Provider,
    string DeviceReference,
    string? WalletReference,
    string AuthenticationMethod);

public sealed record ActivateWalletTokenRequest(
    string ActivationCode);

public sealed record AuthorizeWalletPaymentRequest(
    string TokenReference,
    string ClientTransactionId,
    decimal Amount,
    string CurrencyCode,
    string? MerchantId,
    string? MerchantCategory,
    string? CountryCode,
    bool DeviceAuthenticated,
    DateTimeOffset? PostedOn);

public sealed record WalletTokenView(
    Guid WalletTokenId,
    Guid CardId,
    Guid AccountId,
    Guid CustomerId,
    string Provider,
    string DeviceReference,
    string? WalletReference,
    string TokenReference,
    string AuthenticationMethod,
    string Status,
    string? ActivationHint,
    DateTimeOffset? ActivationExpiresOn,
    DateTimeOffset? ActivatedOn,
    DateTimeOffset? LastUsedOn,
    DateTimeOffset CreatedOn);

public sealed record WalletEnrollmentView(
    WalletTokenView WalletToken,
    string? ActivationCode);

public sealed record WalletAuthorizationView(
    Guid AuthorizationId,
    string ClientTransactionId,
    string TokenReference,
    Guid? AccountId,
    Guid? CardId,
    string Provider,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string ResponseCode,
    string? Reason,
    Guid? HoldId,
    DateTimeOffset AuthorizedOn);
