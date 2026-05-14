namespace CardVault.Api.Contracts;

public sealed record OpenBankingTokenRequest(string GrantType, string ClientId, string ClientSecret, string? Scope);

public sealed record OpenBankingTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);

public sealed record CreateOpenBankingClientRequest(string Name, string[] Scopes, bool AllowAllAccounts);

public sealed record OpenBankingClientResponse(
    string ClientId,
    string Name,
    string[] AllowedScopes,
    bool Enabled,
    bool AllowAllAccounts,
    DateTimeOffset CreatedOn,
    DateTimeOffset UpdatedOn,
    DateTimeOffset? LastTokenIssuedOn,
    Guid[] AuthorizedAccountIds,
    string? ClientSecret);

public sealed record OpenBankingBalanceResponse(
    Guid AccountId,
    Guid CustomerId,
    string CurrencyCode,
    decimal LedgerBalance,
    decimal AvailableLimit,
    decimal HoldBalance,
    string Status,
    DateTimeOffset AsOf);

public sealed record OpenBankingTransactionItem(
    Guid EntryId,
    string Type,
    decimal Amount,
    string Description,
    DateTimeOffset PostedOn);
